#!/usr/bin/env bash
# Build environment for the Hermes Android scaffold (Ataraxia) on this VM.
# Source before running Gradle:  source build-env.sh
# Why each setting exists (verified 2026-10-11):
# - GRADLE_USER_HOME: caches/daemon live in ~/workspace/.gradle (NOT /root/.gradle).
# - preferIPv4Stack: localhost resolves ::1 first on this VM but the Gradle
#   daemon binds IPv4-only; without this the client cannot reach the daemon.
# - trustStore: /usr/lib/jvm/java-21-openjdk-amd64/lib/security/cacerts is a
#   dead symlink, so Java TLS uses ~/workspace/.ssl/cacerts.jks (122 CAs).
# - Daemon JVM args live in gradle.properties (org.gradle.jvmargs); GRADLE_OPTS
#   only affects the short-lived client JVM.
# - Do NOT use --no-daemon here: single-use daemons die instantly on this VM
#   ("Daemon vm is shutting down..." right after "started executing the build").
# - NEVER pkill Gradle daemons while a build may be running. The "daemon
#   disappeared unexpectedly" failures on 2026-10-11 were all SIGTERM kills
#   from concurrent `pkill -f GradleDaemon` cleanups (the victims look like
#   instant deaths because Gradle buffers the build log; only the unbuffered
#   shutdown-hook line survives). To stop idle daemons, use a scoped graceful
#   stop instead:
#     GRADLE_USER_HOME=<that-build's-user-home> gradle --stop
# - The build is quiet for minutes during dependency resolution (logs are
#   buffered). Do NOT assume it is stuck; a full clean build takes ~10 min.

export GRADLE_USER_HOME="$HOME/workspace/.gradle"
export JAVA_HOME="/usr/lib/jvm/java-21-openjdk-amd64"
export ANDROID_HOME="$HOME/workspace/android-sdk"
export ANDROID_SDK_ROOT="$ANDROID_HOME"

_TRUSTSTORE="$HOME/workspace/.ssl/cacerts.jks"
export GRADLE_OPTS="-Djava.net.preferIPv4Stack=true -Djavax.net.ssl.trustStore=${_TRUSTSTORE} -Djavax.net.ssl.trustStorePassword=changeit"
unset _TRUSTSTORE

export PATH="$HOME/workspace/tools/gradle-8.10/bin:$PATH"
