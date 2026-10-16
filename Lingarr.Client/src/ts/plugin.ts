import { LabelValue } from '@/ts'

export const PLUGIN_SETTING_TYPE = {
    TEXT: 'Text',
    URL: 'Url',
    SECRET: 'Secret',
    REMOTE_DROPDOWN: 'RemoteDropdown',
    DROPDOWN: 'Dropdown'
} as const

export type PluginSettingType = (typeof PLUGIN_SETTING_TYPE)[keyof typeof PLUGIN_SETTING_TYPE]

export interface IPluginSettingField {
    key: string
    label: string
    type: PluginSettingType
    required: boolean
    default?: string | null
    description?: string | null
    optionsEndpoint?: string | null
    options?: LabelValue[] | null
    allowCustom?: boolean | null
    minLength?: number | null
    validationErrorMessage?: string | null
}

export interface IPluginSummary {
    provider: string
    displayName: string
    description?: string | null
    isBuiltIn: boolean
    sourceFile?: string | null
    hasRequestTemplate: boolean
    supportsConnectionTest?: boolean
    supportsModelOverride?: boolean
}

export interface IPluginManifest extends IPluginSummary {
    settings: IPluginSettingField[]
}

export interface IPluginStatus {
    provider: string
    configured: boolean
    missingFields: string[]
}

export interface IPluginOptionsResponse {
    options?: LabelValue[] | null
    message?: string | null
}

export interface IPluginTestConnectionResponse {
    success: boolean
    message: string
    options?: LabelValue[] | null
}
