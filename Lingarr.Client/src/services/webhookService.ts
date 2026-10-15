import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { IArrWebhookSecretResponse, IWebhookService } from '@/ts'

const service = (http: AxiosStatic, resource = '/api/webhook'): IWebhookService => ({
    getArrSecret(): Promise<IArrWebhookSecretResponse> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/arr-secret`)
                .then((response: AxiosResponse<IArrWebhookSecretResponse>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },
    regenerateArrSecret(): Promise<IArrWebhookSecretResponse> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/arr-secret`)
                .then((response: AxiosResponse<IArrWebhookSecretResponse>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    }
})

export const webhookService = (axios: AxiosStatic): IWebhookService => {
    return service(axios)
}
