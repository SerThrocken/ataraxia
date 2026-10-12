package com.tlg3d.hermes.data

import android.content.SharedPreferences

/**
 * Minimal string key/value storage. The registries program against this
 * interface (not SharedPreferences directly) so their logic is pure-JVM
 * unit-testable; production wires [SharedPrefsKeyValueStore].
 */
interface KeyValueStore {
    fun getString(key: String): String?
    fun putString(key: String, value: String)
    fun remove(key: String)
}

class SharedPrefsKeyValueStore(private val prefs: SharedPreferences) : KeyValueStore {
    override fun getString(key: String): String? = prefs.getString(key, null)
    override fun putString(key: String, value: String) {
        prefs.edit().putString(key, value).apply()
    }
    override fun remove(key: String) {
        prefs.edit().remove(key).apply()
    }
}
