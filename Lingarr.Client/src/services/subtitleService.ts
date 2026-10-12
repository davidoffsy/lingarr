import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import {
    IExtractAndTranslateRequest,
    IExtractAndTranslateResponse,
    IProbeEmbeddedResponse,
    ISubtitleService,
    MediaType
} from '@/ts'

const service = (http: AxiosStatic, resource = '/api/subtitle'): ISubtitleService => ({
    collect<T>(path: string): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(
                `${resource}/all`,
                {
                    path: path
                },
                {
                    headers: {
                        'Cache-Control': 'no-cache, no-store, must-revalidate',
                        Pragma: 'no-cache',
                        Expires: '0'
                    }
                }
            )
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    probe(
        mediaId: number,
        mediaType: MediaType,
        signal?: AbortSignal
    ): Promise<IProbeEmbeddedResponse> {
        return new Promise((resolve, reject) => {
            http.post<IProbeEmbeddedResponse>(
                `${resource}/probe`,
                {
                    mediaId,
                    mediaType
                },
                {
                    timeout: 60000,
                    signal,
                    headers: {
                        'Cache-Control': 'no-cache, no-store, must-revalidate',
                        Pragma: 'no-cache',
                        Expires: '0'
                    }
                }
            )
                .then((response: AxiosResponse<IProbeEmbeddedResponse>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    if (error.code === 'ERR_CANCELED' || error.name === 'CanceledError') {
                        reject(error)
                        return
                    }
                    reject(error.response ?? error)
                })
        })
    },
    extractAndTranslate(
        request: IExtractAndTranslateRequest
    ): Promise<IExtractAndTranslateResponse> {
        return new Promise((resolve, reject) => {
            http.post<IExtractAndTranslateResponse>(`${resource}/extract-and-translate`, request, {
                headers: {
                    'Cache-Control': 'no-cache, no-store, must-revalidate',
                    Pragma: 'no-cache',
                    Expires: '0'
                }
            })
                .then((response: AxiosResponse<IExtractAndTranslateResponse>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response ?? error)
                })
        })
    }
})

export const subtitleService = (axios: AxiosStatic): ISubtitleService => {
    return service(axios)
}
