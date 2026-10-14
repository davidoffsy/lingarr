export const PLEX_AUTH_WINDOW_NAME = 'lingarr-plex-auth'
export const PLEX_AUTH_WINDOW_FEATURES = 'width=800,height=700'

export const openPlexAuthWindow = (): Window | null =>
    window.open('about:blank', PLEX_AUTH_WINDOW_NAME, PLEX_AUTH_WINDOW_FEATURES)

export const showPlexAuthWaitingPage = (popup: Window): void => {
    try {
        popup.document.open()
        popup.document.write(
            '<!doctype html><html><head><meta charset="utf-8"><title>Plex</title></head>' +
                '<body><p style="font-family:sans-serif;padding:2rem">Redirecting to Plex…</p></body></html>'
        )
        popup.document.close()
    } catch {
        // Some browsers lock about:blank; navigation still proceeds.
    }
}

export const navigatePlexAuthWindow = (popup: Window | null, authUrl: string): boolean => {
    if (!popup || popup.closed) {
        return false
    }

    popup.location.replace(authUrl)
    popup.focus()
    return true
}

export const closePlexAuthWindow = (popup: Window | null): void => {
    if (!popup) {
        return
    }

    try {
        popup.close()
    } catch {
        // Plex login may set COOP and detach the handle.
    }
}
