package com.tlg3d.hermes.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val DarkColors = darkColorScheme(
    primary = Color(0xFF9DCBFF),
    onPrimary = Color(0xFF003355),
    primaryContainer = Color(0xFF004B7A),
    surface = Color(0xFF121212),
    surfaceContainer = Color(0xFF1A1A1A),
)

private val LightColors = lightColorScheme(
    primary = Color(0xFF0B5FA5),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFD6EBFF),
    surface = Color(0xFFFAFAFA),
    surfaceContainer = Color(0xFFF0F0F0),
)

/**
 * Material 3 theme (Expressive direction: tonal surfaces, rounded shapes).
 * Follows the system dark/light setting.
 */
@Composable
fun HermesTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (isSystemInDarkTheme()) DarkColors else LightColors,
        content = content,
    )
}
