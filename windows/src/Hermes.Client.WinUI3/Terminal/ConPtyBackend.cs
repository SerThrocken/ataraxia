using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Hermes.Client.Core.Terminal;
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
/// reader thread; exit is signaled by a wait on the process handle and by
/// the pipe breaking, whichever comes first (once-only).
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

    private HANDLE _inputWrite; // our end: we write the child's stdin
    private HANDLE _outputRead; // our end: we read the child's stdout
    private HPCON _pseudoConsole;
    private HANDLE _process;
    private HANDLE _thread;
    private HANDLE _job;
    private Process? _watchedProcess;
    private RegisteredWaitHandle? _exitWait;
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

        HANDLE inputRead = default, inputWrite = default;
        HANDLE outputRead = default, outputWrite = default;
        HPCON hpc = default;
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
            _ = PInvoke.InitializeProcThreadAttributeList(null, 1, 0, &attrSize);
            attrList = NativeMemory.Alloc(attrSize);
            if (!PInvoke.InitializeProcThreadAttributeList((LPPROC_THREAD_ATTRIBUTE_LIST)attrList, 1, 0, &attrSize))
            {
                Win32Throw("InitializeProcThreadAttributeList");
            }

            var siex = new STARTUPINFOEXW
            {
                StartupInfo = { cb = (uint)sizeof(STARTUPINFOEXW) },
                lpAttributeList = (PPROC_THREAD_ATTRIBUTE_LIST)attrList,
            };
            if (!PInvoke.UpdateProcThreadAttribute(
                    (LPPROC_THREAD_ATTRIBUTE_LIST)attrList, 0, ProcThreadAttributePseudoConsole,
                    &hpc, (nuint)sizeof(HPCON), null, null))
            {
                Win32Throw("UpdateProcThreadAttribute");
            }

            // CreateProcess may modify the command-line buffer: pass a copy.
            var commandLine = string.IsNullOrWhiteSpace(arguments) ? fileName : fileName + " " + arguments;
            var cmdChars = commandLine.ToCharArray();
            fixed (char* cmdPtr = cmdChars)
            {
                if (!PInvoke.CreateProcess(
                        default, new PWSTR(cmdPtr),
                        (SECURITY_ATTRIBUTES*)null, (SECURITY_ATTRIBUTES*)null, true,
                        (PROCESS_CREATION_FLAGS)(ExtendedStartupinfoPresent | CreateUnicodeEnvironment),
                        (void*)null, default,
                        (STARTUPINFOW*)&siex, &pi))
                {
                    Win32Throw("CreateProcess");
                }
            }

            // Adopted: the pseudoconsole now owns the child's pipe ends.
            _pseudoConsole = hpc;
            hpc = default;
            _process = pi.hProcess;
            _thread = pi.hThread;
            _inputWrite = inputWrite;
            inputWrite = default;
            _outputRead = outputRead;
            outputRead = default;
            PInvoke.CloseHandle(inputRead);
            inputRead = default;
            PInvoke.CloseHandle(outputWrite);
            outputWrite = default;

            // Job object: closing it kills the whole tree (KILL_ON_JOB_CLOSE).
            // Best effort — if the process is already in a job this fails and
            // TerminateProcess still kills the direct child.
            var job = PInvoke.CreateJobObject((SECURITY_ATTRIBUTES*)null, default);
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

            // Prompt exit signal independent of the pipes.
            _watchedProcess = Process.GetProcessById((int)pi.dwProcessId);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(
                _watchedProcess.WaitHandle,
                static (state, _) => ((ConPtyBackend)state!).OnProcessSignaled(),
                this, Timeout.Infinite, executeOnlyOnce: true);

            _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "ConPTY output reader" };
            _readerThread.Start();
            _started = true;
        }
        finally
        {
            if (attrList is not null)
            {
                PInvoke.DeleteProcThreadAttributeList((LPPROC_THREAD_ATTRIBUTE_LIST)attrList);
                NativeMemory.Free(attrList);
            }

            if (!_started)
            {
                // Failure path: close everything not adopted above, plus
                // anything adopted before the failure (e.g. CreateProcess
                // succeeded but the exit-watch registration threw — the
                // orphaned child must be killed, not leaked).
                if (!inputRead.IsNull)
                {
                    PInvoke.CloseHandle(inputRead);
                }

                if (!inputWrite.IsNull)
                {
                    PInvoke.CloseHandle(inputWrite);
                }

                if (!outputRead.IsNull)
                {
                    PInvoke.CloseHandle(outputRead);
                }

                if (!outputWrite.IsNull)
                {
                    PInvoke.CloseHandle(outputWrite);
                }

                if (!hpc.IsNull)
                {
                    PInvoke.ClosePseudoConsole(hpc);
                }

                if (!pi.hProcess.IsNull)
                {
                    PInvoke.TerminateProcess(pi.hProcess, 1);
                    PInvoke.CloseHandle(pi.hProcess);
                }

                if (!pi.hThread.IsNull)
                {
                    PInvoke.CloseHandle(pi.hThread);
                }

                _exitWait?.Unregister(null);
                _exitWait = null;
                _watchedProcess?.Dispose();
                _watchedProcess = null;

                if (!_pseudoConsole.IsNull)
                {
                    PInvoke.ClosePseudoConsole(_pseudoConsole);
                    _pseudoConsole = default;
                }

                if (!_inputWrite.IsNull)
                {
                    PInvoke.CloseHandle(_inputWrite);
                    _inputWrite = default;
                }

                if (!_outputRead.IsNull)
                {
                    PInvoke.CloseHandle(_outputRead);
                    _outputRead = default;
                }

                if (!_process.IsNull)
                {
                    PInvoke.TerminateProcess(_process, 1);
                    PInvoke.CloseHandle(_process);
                    _process = default;
                }

                if (!_thread.IsNull)
                {
                    PInvoke.CloseHandle(_thread);
                    _thread = default;
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
        if (data.IsEmpty || _disposed != 0 || _inputWrite.IsNull)
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
        if (_disposed != 0 || _pseudoConsole.IsNull)
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
        _exitWait?.Unregister(null);
        _exitWait = null;

        // Closing the output handle unblocks the reader thread's ReadFile.
        if (!_outputRead.IsNull)
        {
            PInvoke.CloseHandle(_outputRead);
            _outputRead = default;
        }

        _readerThread?.Join(TimeSpan.FromSeconds(2));
        _readerThread = null;

        if (!_inputWrite.IsNull)
        {
            PInvoke.CloseHandle(_inputWrite);
            _inputWrite = default;
        }

        if (!_pseudoConsole.IsNull)
        {
            PInvoke.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = default;
        }

        if (!_process.IsNull)
        {
            PInvoke.CloseHandle(_process);
            _process = default;
        }

        if (!_thread.IsNull)
        {
            PInvoke.CloseHandle(_thread);
            _thread = default;
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
                // Blocking read: returns FALSE with ERROR_BROKEN_PIPE when the
                // child (and the console session) is gone, or when Dispose
                // closes the handle to shut us down.
                if (!PInvoke.ReadFile(_outputRead, p, (uint)buffer.Length, &read, null) || read == 0)
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
