package com.tlg3d.hermes.ui

import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Chat
import androidx.compose.material.icons.filled.Forum
import androidx.compose.material.icons.filled.Memory
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.SmartToy
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationDrawerItem
import androidx.compose.material3.NavigationRail
import androidx.compose.material3.NavigationRailItem
import androidx.compose.material3.PermanentDrawerSheet
import androidx.compose.material3.PermanentNavigationDrawer
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.windowsizeclass.ExperimentalMaterial3WindowSizeClassApi
import androidx.compose.material3.windowsizeclass.WindowSizeClass
import androidx.compose.material3.windowsizeclass.WindowWidthSizeClass
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.navigation.NavDestination.Companion.hierarchy
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.navArgument
import androidx.navigation.compose.rememberNavController
import com.tlg3d.hermes.share.SharePayload
import com.tlg3d.hermes.ui.screens.ChatScreen
import com.tlg3d.hermes.ui.screens.ModelsScreen
import com.tlg3d.hermes.ui.screens.SessionsScreen
import com.tlg3d.hermes.ui.screens.SettingsScreen
import com.tlg3d.hermes.ui.screens.SkillsScreen
import com.tlg3d.hermes.ui.screens.VoiceScreen
import com.tlg3d.hermes.ui.screens.SteerScreen

private enum class Destination(
    val route: String,
    val label: String,
    val icon: ImageVector,
) {
    Chat("chat", "Chat", Icons.AutoMirrored.Filled.Chat),
    Sessions("sessions", "Sessions", Icons.Filled.Forum),
    Models("models", "Models", Icons.Filled.SmartToy),
    Voice("voice", "Voice", Icons.Filled.Mic),
    Skills("skills", "Skills", Icons.Filled.Memory),
    Settings("settings", "Settings", Icons.Filled.Settings),
}

/**
 * Single-activity navigation with adaptive layout:
 * compact phone -> bottom bar, medium foldable -> nav rail,
 * expanded tablet -> permanent navigation drawer.
 */
/**
 * @param steerRunId when non-null, a deep-link request (e.g. from the
 * persistent run notification) to open the steer input for that run.
 * Consumed once via [onSteerConsumed].
 */
@OptIn(ExperimentalMaterial3WindowSizeClassApi::class)
@Composable
fun HermesNav(
    windowSizeClass: WindowSizeClass,
    steerRunId: String? = null,
    onSteerConsumed: () -> Unit = {},
    sharedContent: SharePayload? = null,
    onSharedConsumed: () -> Unit = {},
) {
    val navController = rememberNavController()
    LaunchedEffect(steerRunId) {
        if (steerRunId != null) {
            navController.navigate("steer/$steerRunId")
            onSteerConsumed()
        }
    }
    val destinations = Destination.entries
    val backStack by navController.currentBackStackEntryAsState()
    val current = backStack?.destination
    val navigate: (Destination) -> Unit = { dest ->
        navController.navigate(dest.route) {
            popUpTo(navController.graph.findStartDestination().id) { saveState = true }
            launchSingleTop = true
            restoreState = true
        }
    }

    // A share from another app always opens a fresh chat, wherever the user was.
    // ChatScreen consumes the payload (creates the session, prefills the composer).
    LaunchedEffect(sharedContent) {
        if (sharedContent != null) navigate(Destination.Chat)
    }

    val content: @Composable (Modifier) -> Unit = { modifier ->
        NavHost(
            navController = navController,
            startDestination = Destination.Chat.route,
            modifier = modifier,
        ) {
            composable(
                route = "${Destination.Chat.route}?sessionId={sessionId}",
                arguments = listOf(
                    navArgument("sessionId") {
                        type = NavType.StringType
                        nullable = true
                        defaultValue = null
                    },
                ),
            ) { entry ->
                ChatScreen(
                    initialSessionId = entry.arguments?.getString("sessionId"),
                    sharedContent = sharedContent,
                    onSharedConsumed = onSharedConsumed,
                )
            }
            composable(Destination.Voice.route) { VoiceScreen() }
            composable(Destination.Sessions.route) {
                SessionsScreen(onOpenSession = { id ->
                    navController.navigate("${Destination.Chat.route}?sessionId=$id") {
                        popUpTo(navController.graph.findStartDestination().id) { saveState = true }
                        launchSingleTop = true
                        restoreState = true
                    }
                })
            }
            composable(Destination.Models.route) { ModelsScreen() }
            composable(Destination.Skills.route) { SkillsScreen() }
            composable(Destination.Settings.route) { SettingsScreen() }
            // Not a bottom-bar destination: reached via the run notification's
            // "Steer run" action (MainActivity.EXTRA_STEER_RUN_ID deep link).
            composable(
                route = "steer/{runId}",
                arguments = listOf(
                    navArgument("runId") { type = NavType.StringType },
                ),
            ) { entry ->
                SteerScreen(runId = entry.arguments?.getString("runId").orEmpty())
            }
        }
    }

    when (windowSizeClass.widthSizeClass) {
        WindowWidthSizeClass.Compact -> {
            Scaffold(
                bottomBar = {
                    NavigationBar {
                        destinations.forEach { dest ->
                            val selected = current?.hierarchy?.any { it.route == dest.route } == true
                            NavigationBarItem(
                                selected = selected,
                                onClick = { navigate(dest) },
                                icon = { Icon(dest.icon, contentDescription = dest.label) },
                                label = { Text(dest.label) },
                            )
                        }
                    }
                },
            ) { padding -> content(Modifier.padding(padding).fillMaxSize()) }
        }
        WindowWidthSizeClass.Medium -> {
            Row(Modifier.fillMaxSize()) {
                NavigationRail {
                    destinations.forEach { dest ->
                        val selected = current?.hierarchy?.any { it.route == dest.route } == true
                        NavigationRailItem(
                            selected = selected,
                            onClick = { navigate(dest) },
                            icon = { Icon(dest.icon, contentDescription = dest.label) },
                            label = { Text(dest.label) },
                        )
                    }
                }
                content(Modifier.fillMaxSize())
            }
        }
        else -> {
            PermanentNavigationDrawer(
                drawerContent = {
                    PermanentDrawerSheet {
                        destinations.forEach { dest ->
                            val selected = current?.hierarchy?.any { it.route == dest.route } == true
                            NavigationDrawerItem(
                                selected = selected,
                                onClick = { navigate(dest) },
                                icon = { Icon(dest.icon, contentDescription = dest.label) },
                                label = { Text(dest.label) },
                            )
                        }
                    }
                },
            ) { content(Modifier.fillMaxSize()) }
        }
    }
}
