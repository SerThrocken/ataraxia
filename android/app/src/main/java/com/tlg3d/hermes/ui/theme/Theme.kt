package com.tlg3d.hermes.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

/**
 * Ataraxia palette: deep slate + teal, one warm amber accent.
 * Calm, low-chroma surfaces; the amber is reserved for calls to action and
 * warnings so it keeps its meaning. No gradients, no neon.
 */

// Teal (primary)
private val Teal80 = Color(0xFF7FD8C2)
private val Teal40 = Color(0xFF0E6B5B)
private val TealContainerDark = Color(0xFF0C3F36)
private val TealContainerLight = Color(0xFFB7E8D8)

// Warm amber (accent — used sparingly)
private val Amber80 = Color(0xFFE8B45A)
private val Amber40 = Color(0xFF8A5E14)
private val AmberContainerDark = Color(0xFF42300C)
private val AmberContainerLight = Color(0xFFF3DDAE)

// Deep slate neutrals
private val Slate95 = Color(0xFFDCE7E3)
private val Slate80 = Color(0xFF9DB3AC)
private val Slate30 = Color(0xFF3A4A45)
private val Slate20 = Color(0xFF24332F)
private val Slate10 = Color(0xFF0D1417)
private val Slate05 = Color(0xFF090E11)

private val Paper = Color(0xFFF6F4EE)
private val PaperContainerLow = Color(0xFFEDEBE2)
private val PaperContainer = Color(0xFFE6E3D6)
private val PaperContainerHigh = Color(0xFFDDD9C9)
private val Ink = Color(0xFF17201D)
private val InkVariant = Color(0xFF4A5A54)

private val DarkColors = darkColorScheme(
    primary = Teal80,
    onPrimary = Color(0xFF06302A),
    primaryContainer = TealContainerDark,
    onPrimaryContainer = Color(0xFFC4EDE0),
    secondary = Amber80,
    onSecondary = Color(0xFF3A2708),
    secondaryContainer = AmberContainerDark,
    onSecondaryContainer = Color(0xFFF3DDAE),
    tertiary = Slate80,
    background = Slate10,
    onBackground = Slate95,
    surface = Slate10,
    onSurface = Slate95,
    surfaceVariant = Slate20,
    onSurfaceVariant = Slate80,
    surfaceContainerLowest = Slate05,
    surfaceContainerLow = Color(0xFF111A1E),
    surfaceContainer = Color(0xFF16211F),
    surfaceContainerHigh = Color(0xFF1B272B),
    surfaceContainerHighest = Color(0xFF223136),
    outline = Slate30,
    outlineVariant = Slate20,
    error = Color(0xFFFFB4AB),
    onError = Color(0xFF690005),
    errorContainer = Color(0xFF93000A),
    onErrorContainer = Color(0xFFFFDAD6),
)

private val LightColors = lightColorScheme(
    primary = Teal40,
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = TealContainerLight,
    onPrimaryContainer = Color(0xFF06302A),
    secondary = Amber40,
    onSecondary = Color(0xFFFFFFFF),
    secondaryContainer = AmberContainerLight,
    onSecondaryContainer = Color(0xFF3A2708),
    tertiary = Color(0xFF5A6E66),
    background = Paper,
    onBackground = Ink,
    surface = Paper,
    onSurface = Ink,
    surfaceVariant = PaperContainer,
    onSurfaceVariant = InkVariant,
    surfaceContainerLowest = Color(0xFFFFFFFF),
    surfaceContainerLow = PaperContainerLow,
    surfaceContainer = PaperContainer,
    surfaceContainerHigh = PaperContainerHigh,
    surfaceContainerHighest = Color(0xFFD2CEBD),
    outline = Color(0xFF8A988F),
    outlineVariant = Color(0xFFC9CFC6),
    error = Color(0xFFBA1A1A),
    onError = Color(0xFFFFFFFF),
    errorContainer = Color(0xFFFFDAD6),
    onErrorContainer = Color(0xFF410002),
)

/**
 * Ataraxia theme. Follows the system dark/light setting; dark is the
 * primary design target (late-night ops on the Acer).
 */
@Composable
fun HermesTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (isSystemInDarkTheme()) DarkColors else LightColors,
        content = content,
    )
}
