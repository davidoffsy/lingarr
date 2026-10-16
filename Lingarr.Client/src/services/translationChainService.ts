import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { ITranslationChainService, ITranslationChainValidation } from '@/ts'

const service = (
    http: AxiosStatic,
    resource = '/api/plugin/translation-chain'
): ITranslationChainService => ({
    validateTranslationChain(serviceType: string): Promise<ITranslationChainValidation> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/validate`, { serviceType })
                .then((response: AxiosResponse<ITranslationChainValidation>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    }
})

export const translationChainService = (axios: AxiosStatic): ITranslationChainService => {
    return service(axios)
}
