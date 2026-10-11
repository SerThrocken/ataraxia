package com.tlg3d.hermes

import android.app.Application
import com.tlg3d.hermes.security.KeystoreSecretStore
import com.tlg3d.hermes.security.SecretStore

/** App-wide singletons, wired manually (no DI framework at this stage). */
class HermesApp : Application() {
    val secretStore: SecretStore by lazy { KeystoreSecretStore(this) }
}
