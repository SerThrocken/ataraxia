package com.tlg3d.hermes.ssh

/**
 * The server presented an SSH host key that is not pinned for this host.
 * The UI catches this, shows the fingerprint to the user, and — on approval —
 * pins it via [SshHostKeyTrust] and retries the connection. [keyChanged]
 * distinguishes a first-seen key from a changed one (possible
 * man-in-the-middle): the UI must warn louder in the latter case.
 */
class SshUnknownHostKeyException(
    val host: String,
    val port: Int,
    val fingerprint: String,
    val keyChanged: Boolean,
    cause: Throwable? = null,
) : Exception(
    if (keyChanged) {
        "SSH host key for $host:$port CHANGED (possible man-in-the-middle). " +
            "Presented fingerprint: $fingerprint."
    } else {
        "Unknown SSH host key for $host:$port. Presented fingerprint: $fingerprint."
    },
    cause,
)
