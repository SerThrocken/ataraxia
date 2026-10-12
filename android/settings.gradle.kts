pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        // Local file repo inside the Gradle user home (populated by hand with
        // curl on machines where the daemon has no egress proxy for Google /
        // Maven Central — e.g. junit for unit tests on the Linux build VM).
        // Missing on other machines: Gradle treats an absent file repo as
        // empty and falls through to google()/mavenCentral() below.
        maven { url = uri("file://${gradle.gradleUserHomeDir}/local-m2") }
        google()
        mavenCentral()
    }
}
rootProject.name = "hermes-android"
include(":app")
