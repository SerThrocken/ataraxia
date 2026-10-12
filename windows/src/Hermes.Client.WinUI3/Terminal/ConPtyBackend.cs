using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Hermes.Client.Core.Terminal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Console;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.Threading;

namespace Hermes.Client.WinUI3.Terminal;

/// <summary>
/// <see cref="ITerminalBackend"/> over the Windows Pseudo Console (ConPTY)
/// API, via CsWin32-generated P/Invoke (see NativeMethods.txt). Written
/// clean-room against the documented ConPTY recipe: two pipes, a
/// pseudoconsole, and a child spawned with
/// <c>EXTENDED_STARTUPINFO_PRESENT</c> + the pseudo-console thread
/// attribute. The child is placed in a job object with
/// <c>KILL_ON_JOB_CLOSE</c> so disposing the session kills the whole
/// process tree — no orphaned shells. Output is pumped on a dedicated
/// reader thread; exit is signaled by the process Exited event and by the
/// pipe breaking, whichever comes first (once-only).
/// </summary>
public sealed class ConPtyBackend : ITerminalBackend
{
    // Documented SDK constant values (winbase.h / winnt.h); plain numbers,
    // not copied code.
    private const uint ExtendedStartupinfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint StillActive = 259;
    private static readonly nuint ProcThreadAttributePseudoConsole = 0x00020016; // 22 | PROC_THREAD_ATTRIBUTE_INPUT

    // STARTUPINFOEX is not emitted by CsWin32 (CreateProcess only references
    // STARTUPINFO), so the two-field extension is defined here from the
    // documented layout.
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFOW StartupInfo;
        public IntPtr lpAttributeList;
    }

    // Likewise not emitted (SetInformationJobObject takes void*): minimal
    // definitions from the documented layouts; only LimitFlags is ever set.
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private SafeFileHandle? _inputWrite; // our end: we write the child's stdin
    private SafeFileHandle? _outputRead; // our end: we read the child's stdout
    private ClosePseudoConsoleSafeHandle? _pseudoConsole;
    private HANDLE _process; // raw: kept for TerminateProcess/GetExitCodeProcess
    private HANDLE _job;
    private Process? _watchedProcess;
    private Thread? _readerThread;
    private int _exitSignaled;
    private int _disposed;
    private bool _started;

    public event EventHandler<ReadOnlyMemory<byte>>? OutputReceived;
    public event EventHandler<int>? Exited;

    public unsafe void Start(string fileName, string arguments, int cols, int rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (_started)
        {
            throw new InvalidOperationException("Backend already started.");
        }

        SafeFileHandle? inputRead = null;
        SafeFileHandle? inputWrite = null;
        SafeFileHandle? outputRead = null;
        SafeFileHandle? outputWrite = null;
        ClosePseudoConsoleSafeHandle? hpc = null;
        void* attrList = null;
        PROCESS_INFORMATION pi = default;

        try
        {
            var sa = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                bInheritHandle = true,
            };
            if (!PInvoke.CreatePipe(out inputRead, out inputWrite, &sa, 0))
            {
                Win32Throw("CreatePipe (stdin)");
            }

            if (!PInvoke.CreatePipe(out outputRead, out outputWrite, &sa, 0))
            {
                Win32Throw("CreatePipe (stdout)");
            }

            // Our ends must not be inherited by the child: the child talks
            // to the pseudoconsole, not to our pipe ends. (If it inherited
            // them, the output pipe would never break on exit.)
            PInvoke.SetHandleInformation(inputWrite, HandleFlagInherit, 0);
            PInvoke.SetHandleInformation(outputRead, HandleFlagInherit, 0);

            var size = new COORD
            {
                X = (short)Math.Clamp(cols, 1, 999),
                Y = (short)Math.Clamp(rows, 1, 999),
            };
            var hr = PInvoke.CreatePseudoConsole(size, inputRead, outputWrite, 0, out hpc);
            if (hr.Failed)
            {
                Win32Throw($"CreatePseudoConsole (HRESULT 0x{hr.Value:X8})");
            }

            nuint attrSize = 0;
            _ = PInvoke.InitializeProcThreadAttributeList(default, 1, 0, &attrSize);
            attrList = NativeMemory.Alloc(attrSize);
            var attrListTyped = new LPPROC_THREAD_ATTRIBUTE_LIST(attrList);
            if (!PInvoke.InitializeProcThreadAttributeList(attrListTyped, 1, 0, &attrSize))
            {
                Win32Throw("InitializeProcThreadAttributeList");
            }

            var siex = new STARTUPINFOEX
            {
                StartupInfo = { cb = (uint)sizeof(STARTUPINFO) },
                lpAttributeList = (IntPtr)attrList,
            };
            var hpcHandle = hpc.DangerousGetHandle();
            if (!PInvoke.UpdateProcThreadAttribute(
                    attrListTyped, 0, ProcThreadAttributePseudoConsole,
                    &hpcHandle, (nuint)sizeof(nint), null, null))
            {
                Win32Throw("UpdateProcThreadAttribute");
            }

            // CreateProcess may modify the command-line buffer: pass a copy.
            var commandLine = string.IsNullOrWhiteSpace(arguments) ? fileName : fileName + " " + arguments;
            var cmdChars = commandLine.ToCharArray();
            fixed (char* cmdPtr = cmdChars)
            fixed (STARTUPINFOEX* siexPtr = &siex)
            {
                if (!PInvoke.CreateProcess(
                        default, new PWSTR(cmdPtr),
                        null, null, true,
                        (PROCESS_CREATION_FLAGS)(ExtendedStartupinfoPresent | CreateUnicodeEnvironment),
                        null, default,
                        (STARTUPINFO*)siexPtr, &pi))
                {
                    Win32Throw("CreateProcess");
                }
            }

            // Adopted: the pseudoconsole now owns the child's pipe ends.
            _pseudoConsole = hpc;
            hpc = null;
            _process = pi.hProcess;
            _inputWrite = inputWrite;
            inputWrite = null;
            _outputRead = outputRead;
            outputRead = null;
            inputRead?.Dispose();
            inputRead = null;
            outputWrite?.Dispose();
            outputWrite = null;
            // pi.hThread is not needed; close it now.
            if (!pi.hThread.IsNull)
            {
                PInvoke.CloseHandle(pi.hThread);
            }

            // Job object: closing it kills the whole tree (KILL_ON_JOB_CLOSE).
            // Best effort — if the process is already in a job this fails and
            // TerminateProcess still kills the direct child.
            var job = PInvoke.CreateJobObject(null, default);
            if (!job.IsNull)
            {
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                if (PInvoke.SetInformationJobObject(job, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                        &info, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))
                    && PInvoke.AssignProcessToJobObject(job, _process))
                {
                    _job = job;
                }
                else
                {
                    PInvoke.CloseHandle(job);
                }
            }

            // Exit signal independent of the pipes.
            _watchedProcess = Process.GetProcessById((int)pi.dwProcessId);
            _watchedProcess.EnableRaisingEvents = true;
            _watchedProcess.Exited += (_, _) => OnProcessSignaled();

            _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "ConPTY output reader" };
            _readerThread.Start();
            _started = true;
        }
        finally
        {
            if (attrList is not null)
            {
                PInvoke.DeleteProcThreadAttributeList(new LPPROC_THREAD_ATTRIBUTE_LIST(attrList));
                NativeMemory.Free(attrList);
            }

            if (!_started)
            {
                // Failure path: release everything not adopted above. Anything
                // adopted before the failure (e.g. CreateProcess succeeded but
                // the exit-watch registration threw) must also be cleaned up:
                // the orphaned child is killed, not leaked.
                inputRead?.Dispose();
                inputWrite?.Dispose();
                outputRead?.Dispose();
                outputWrite?.Dispose();
                hpc?.Dispose();

                if (!pi.hProcess.IsNull)
                {
                    PInvoke.TerminateProcess(pi.hProcess, 1);
                    PInvoke.CloseHandle(pi.hProcess);
                }

                if (!pi.hThread.IsNull)
                {
                    PInvoke.CloseHandle(pi.hThread);
                }

                _watchedProcess?.Dispose();
                _watchedProcess = null;

                _pseudoConsole?.Dispose();
                _pseudoConsole = null;
                _inputWrite?.Dispose();
                _inputWrite = null;
                _outputRead?.Dispose();
                _outputRead = null;

                if (!_process.IsNull)
                {
                    PInvoke.TerminateProcess(_process, 1);
                    PInvoke.CloseHandle(_process);
                    _process = default;
                }

                if (!_job.IsNull)
                {
                    PInvoke.CloseHandle(_job);
                    _job = default;
                }
            }
        }
    }

    public unsafe void WriteInput(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || _disposed != 0 || _inputWrite is null || _inputWrite.IsInvalid)
        {
            return;
        }

        fixed (byte* p = data)
        {
            var offset = 0;
            while (offset < data.Length)
            {
                uint written = 0;
                if (!PInvoke.WriteFile(_inputWrite, p + offset, (uint)(data.Length - offset), &written, null)
                    || written == 0)
                {
                    break; // pipe broken: the child is gone; exit gets signaled
                }

                offset += (int)written;
            }
        }
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed != 0 || _pseudoConsole is null || _pseudoConsole.IsInvalid)
        {
            return;
        }

        // Best effort: a failed resize keeps the old size; the next one retries.
        _ = PInvoke.ResizePseudoConsole(_pseudoConsole, new COORD
        {
            X = (short)Math.Clamp(cols, 1, 999),
            Y = (short)Math.Clamp(rows, 1, 999),
        });
    }

    public void Terminate()
    {
        if (!_process.IsNull)
        {
            PInvoke.TerminateProcess(_process, 1);
        }

        // Closing the job handle kills the whole child tree (KILL_ON_JOB_CLOSE).
        if (!_job.IsNull)
        {
            PInvoke.CloseHandle(_job);
            _job = default;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Terminate();

        // Closing the output handle unblocks the reader thread's ReadFile.
        _outputRead?.Dispose();
        _outputRead = null;

        _readerThread?.Join(TimeSpan.FromSeconds(2));
        _readerThread = null;

        _inputWrite?.Dispose();
        _inputWrite = null;

        _pseudoConsole?.Dispose();
        _pseudoConsole = null;

        if (!_process.IsNull)
        {
            PInvoke.CloseHandle(_process);
            _process = default;
        }

        _watchedProcess?.Dispose();
        _watchedProcess = null;
    }

    private unsafe void ReaderLoop()
    {
        var buffer = new byte[8192];
        fixed (byte* p = buffer)
        {
            while (true)
            {
                uint read = 0;
                var handle = _outputRead;
                if (handle is null || handle.IsInvalid)
                {
                    break;
                }

                // Blocking read: returns FALSE with ERROR_BROKEN_PIPE when the
                // child (and the console session) is gone, or when Dispose
                // closes the handle to shut us down.
                if (!PInvoke.ReadFile(handle, p, (uint)buffer.Length, &read, null) || read == 0)
                {
                    break;
                }

                var copy = new byte[read];
                Buffer.BlockCopy(buffer, 0, copy, 0, (int)read);
                OutputReceived?.Invoke(this, copy);
            }
        }

        if (_disposed != 0)
        {
            return;
        }

        uint code = 0;
        if (!_process.IsNull && PInvoke.GetExitCodeProcess(_process, out code) && code != StillActive)
        {
            SignalExited((int)code);
        }
    }

    private void OnProcessSignaled()
    {
        if (_disposed != 0)
        {
            return;
        }

        uint code = 0;
        if (!_process.IsNull && PInvoke.GetExitCodeProcess(_process, out code) && code != StillActive)
        {
            SignalExited((int)code);
        }
    }

    private void SignalExited(int code)
    {
        if (Interlocked.Exchange(ref _exitSignaled, 1) != 0)
        {
            return;
        }

        Exited?.Invoke(this, code);
    }

    private static void Win32Throw(string what)
    {
        var code = Marshal.GetLastPInvokeError();
        throw new InvalidOperationException($"{what} failed (Win32 error {code}).");
    }
}
