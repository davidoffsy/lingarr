export interface IPlexPinResponse {
    pinId: number
    code: string
    authUrl: string
}

export interface IPlexPinStatus {
    linked: boolean
    accountTitle?: string
}

export interface IPlexConnection {
    uri: string
    local: boolean
    relay: boolean
}

export interface IPlexServer {
    name: string
    machineId: string
    preferredUri?: string
    connections: IPlexConnection[]
}

export interface IPlexTestRequest {
    url?: string
}

export interface IPlexConnectionTestResult {
    success: boolean
    machineIdentifier?: string
    version?: string
    message?: string
}

export interface IPlexWebhookSecretResponse {
    secret: string
}

export interface IPlexWebhookStatus {
    receivedAt: string
    outcome: string
    httpStatus: number
    reason?: string
    event?: string
    title?: string
    rating?: number
    accountTitle?: string
}
