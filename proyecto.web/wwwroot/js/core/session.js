const KEYS = { access: 'auth.accessToken', refresh: 'auth.refreshToken' };
const REPORT_TOKEN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export const ReportNavigation = {
    remember(search) {
        const token = new URLSearchParams(search).get('reportar');
        if (!REPORT_TOKEN.test(token ?? '')) return;

        try {
            sessionStorage.setItem('report.pending', JSON.stringify({ token, expires: Date.now() + 15 * 60000 }));
        } catch { /* continuidad opcional */ }
    },
    take() {
        try {
            const pending = JSON.parse(sessionStorage.getItem('report.pending') ?? 'null');
            sessionStorage.removeItem('report.pending');
            return pending
                && REPORT_TOKEN.test(pending.token)
                && pending.expires > Date.now()
                && !TokenStore.esAdmin()
                && !TokenStore.esGerente()
                ? `dashboard-usuario.html?reportar=${encodeURIComponent(pending.token)}` : null;
        } catch { return null; }
    },
};

export function decodeToken(token) {
    try {
        const encoded = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
        return JSON.parse(new TextDecoder().decode(Uint8Array.from(
            atob(encoded.padEnd(Math.ceil(encoded.length / 4) * 4, '=')),
            c => c.charCodeAt(0))));
    } catch { return null; }
}

export const TokenStore = {
    getAccessToken: () => localStorage.getItem(KEYS.access),
    getRefreshToken: () => localStorage.getItem(KEYS.refresh),
    setTokens({ token, refreshToken } = {}) {
        if (token) localStorage.setItem(KEYS.access, token);
        if (refreshToken) localStorage.setItem(KEYS.refresh, refreshToken);
    },
    clear() { Object.values(KEYS).forEach(key => localStorage.removeItem(key)); },
    getDecodedToken() { return decodeToken(this.getAccessToken()); },
    esAdmin() {
        return this.roles().includes('Admin');
    },
    roles() {
        const claims = this.getDecodedToken();
        const roles = claims?.role ?? claims?.['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ?? [];
        return Array.isArray(roles) ? roles : [roles];
    },
    esGerente() { return !this.esAdmin() && this.roles().includes('GerenteSucursal'); },
    puedeAbrir(page) {
        if (page === 'seguridad-mfa' || this.esAdmin()) return true;

        return this.esGerente() ? ['seguimiento-preventivo', 'cyberscore'].includes(page) : page === 'dashboard-usuario';
    },
    isValid() { return Number(this.getDecodedToken()?.exp) * 1000 > Date.now() + 15000; },
    home() {
        return this.esAdmin() ? 'dashboard.html' : this.esGerente() ? 'seguimiento-preventivo.html' : 'dashboard-usuario.html';
    },
    identity() {
        const claims = this.getDecodedToken() ?? {};
        return {
            name: claims.unique_name ?? claims.name ?? claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'] ?? 'Mi cuenta',
            email: claims.email ?? claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'] ?? '',
        };
    },
};
