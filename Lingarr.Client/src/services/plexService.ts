import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import {
    IPlexService,
    IPlexPinResponse,
    IPlexPinStatus,
    IPlexServer,
    IPlexConnectionTestResult,
    IPlexWebhookSecretResponse,
    IPlexWebhookStatus
} from '@/ts'

const service = (http: AxiosStatic, resource = '/api/plex'): IPlexService => ({
    createPin(): Promise<IPlexPinResponse> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/auth/pin`)
                .then((response: AxiosResponse<IPlexPinResponse>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },
    pollPin(id: number): Promise<IPlexPinStatus> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/auth/pin/${id}`)
                .then((response: AxiosResponse<IPlexPinStatus>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },
    disconnect(): Promise<void> {
        return new Promise((resolve, reject) => {
            http.delete(`${resource}/auth`)
                .then(() => resolve())
                .catch((error: AxiosError) => reject(error.response))
        })
    },
    getServers(): Promise<IPlexServer[]> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/servers`)
                .then((response: AxiosResponse<IPlexServer[]>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },
    testConnection(url?: string): Promise<IPlexConnectionTestResult> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/test`, url ? { url } : {})
                .then((response: AxiosResponse<IPlexConnectionTestResult>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },
    getWebhookSecret(): Promise<IPlexWebhookSecretResponse> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/webhook-secret`)
                .then((response: AxiosResponse<IPlexWebhookSecretResponse>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },
    regenerateWebhookSecret(): Promise<IPlexWebhookSecretResponse> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/webhook-secret`)
                .then((response: AxiosResponse<IPlexWebhookSecretResponse>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },
    getWebhookStatus(): Promise<IPlexWebhookStatus | null> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/webhook-status`)
                .then((response: AxiosResponse<IPlexWebhookStatus | null>) => resolve(response.data ?? null))
                .catch((error: AxiosError) => reject(error.response))
        })
    }
})

export const plexService = (axios: AxiosStatic): IPlexService => {
    return service(axios)
}
