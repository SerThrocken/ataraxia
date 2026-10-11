package com.tlg3d.hermes.network

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/**
 * Wire models for the Hermes gateway API.
 *
 * Minimal on purpose: exact response shapes are being verified in parallel
 * (api-contract.md). Fields are nullable/defaulted and the JSON parser ignores
 * unknown keys so the scaffold survives shape refinement without edits.
 */
val HermesJson = Json {
    ignoreUnknownKeys = true
    isLenient = true
}

@Serializable
data class Session(
    val id: String,
    val title: String? = null,
    val model: String? = null,
    val createdAt: String? = null,
)

@Serializable
data class ChatMessage(
    val role: String,
    val content: String,
)

@Serializable
data class ModelInfo(
    val id: String,
    val name: String? = null,
)

@Serializable
data class SkillInfo(
    val name: String,
    val description: String? = null,
)
