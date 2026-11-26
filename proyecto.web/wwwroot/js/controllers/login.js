import { icon } from '../components/icons.js';
import { apiCall } from '../services/api.js';
import { TokenStore, ReportNavigation } from '../core/session.js';
import { escapeHtml as esc, conCarga } from '../utils/ui.js';

// vistas de login: login, cambio de password obligatorio, MFA y olvide mi password

(function () {
    // nombres de proveedor de ASP.NET Identity, no hay verificacion por SMS
    const METODOS = {
        Email: {
            label: 'Código por correo',
            hint: 'Te enviamos un código a tu correo registrado.',
            icon: () => icon('mail')
        },
        Authenticator: {
            label: 'App autenticadora',
            hint: 'Abre tu app de autenticación e ingresa el código actual.',
            icon: () => icon('verified')
        },
    };

    // estado de la verificacion en curso, solo en memoria
    const estado = {
        cambioPasswordToken: null,
        mfaToken: null,
        metodoSeleccionado: null,
    };

    const vistas = {};
    document.querySelectorAll('.view').forEach((el) => { vistas[el.id] = el; });

    function mostrarVista(id) {
        Object.values(vistas).forEach((v) => v.classList.remove('is-active'));
        vistas[id].classList.add('is-active');
        ocultarError(id);
    }

    function idErrorDeVista(vistaId) {
        return {
            'view-login': 'login-error',
            'view-cambio-obligatorio': 'cambio-error',
            'view-mfa-metodo': 'mfa-metodo-error',
            'view-mfa-recovery': 'recovery-error',
            'view-forgot': 'forgot-error',
        }[vistaId];
    }

    function mostrarError(vistaId, mensaje) {
        const id = idErrorDeVista(vistaId);
        if (!id) return;

        const el = document.getElementById(id);
        el.textContent = mensaje;
        el.classList.add('is-visible');
    }

    function ocultarError(vistaId) {
        const id = idErrorDeVista(vistaId);
        if (!id) return;

        const el = document.getElementById(id);
        el.classList.remove('is-visible');
        el.textContent = '';
    }

    // mostrar u ocultar contrasena

    // punto unico de manejo de LoginResponse, desde /login y /cambiar-password-obligatorio
    function manejarLoginResponse(data) {
        if (data.requiereCambioPassword) {
            estado.cambioPasswordToken = data.cambioPasswordToken;
            mostrarVista('view-cambio-obligatorio');
            return;
        }

        if (data.requiereMfa) {
            estado.mfaToken = data.mfaToken;
            renderMetodosMfa(data.metodosDisponibles || []);
            mostrarVista('view-mfa-metodo');
            return;
        }

        if (data.token) {
            finalizarSesion(data.token, (data.refreshToken ?? data.refreshtoken));
            return;
        }

        mostrarError('view-login', 'Respuesta inesperada del servidor.');
    }

    function finalizarSesion(token, refreshToken) {
        TokenStore.setTokens({ token, refreshToken });

        window.location.replace(ReportNavigation.take() || TokenStore.home());
    }

    // vista: login
    const formLogin = document.getElementById('form-login');
    formLogin.addEventListener('submit', (e) => {
        e.preventDefault();
        const usuario = document.getElementById('login-usuario').value.trim();
        const password = document.getElementById('login-password').value;

        let valido = true;
        marcarCampo('login-usuario', usuario.length > 0);
        marcarCampo('login-password', password.length > 0);
        if (!usuario || !password) valido = false;
        if (!valido) return;

        conCarga(document.getElementById('login-submit'), async () => {
            try {
                const data = await apiCall('/auth/login', {
                    body: { nombreUsuario: usuario, password },
                });
                manejarLoginResponse(data);
            } catch (err) {
                mostrarError('view-login', err.message);
            }
        })();
    });

    function marcarCampo(inputId, ok) {
        const field = document.getElementById(inputId).closest('.field');
        const errorEl = field.querySelector('.field-error');
        field.classList.toggle('has-error', !ok);
        if (errorEl) errorEl.classList.toggle('is-visible', !ok);
    }

    // vista: cambio de password obligatorio
    document.getElementById('form-cambio-obligatorio').addEventListener('submit', (e) => {
        e.preventDefault();
        const nueva = document.getElementById('cambio-password-nueva').value;
        const confirmar = document.getElementById('cambio-password-confirmar').value;

        const coinciden = nueva.length >= 12 && nueva === confirmar;
        marcarCampo('cambio-password-confirmar', coinciden);
        if (!coinciden) return;

        conCarga(document.getElementById('cambio-submit'), async () => {
            try {
                const data = await apiCall('/auth/cambiar-password-obligatorio', {
                    body: { cambioPasswordToken: estado.cambioPasswordToken, passwordNueva: nueva },
                });
                manejarLoginResponse(data);
            } catch (err) {
                mostrarError('view-cambio-obligatorio', err.message);
            }
        })();
    });

    // vista: elegir metodo MFA e ingresar codigo
    function renderMetodosMfa(metodos) {
        const lista = document.getElementById('mfa-method-list');
        lista.innerHTML = '';
        document.getElementById('mfa-code-block').classList.add('hidden');
        estado.metodoSeleccionado = null;
        // los codigos de recuperacion solo existen si se configuro la app autenticadora
        document.getElementById('go-mfa-recovery').hidden = !metodos.includes('Authenticator');

        metodos.forEach((metodo) => {
            const info = METODOS[metodo] || { label: metodo, hint: '', icon: () => icon('verified') };
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'method-option';
            btn.dataset.metodo = metodo;
            btn.innerHTML = `
        <span class="method-option__icon">${info.icon()}</span>
        <span class="method-option__body">
          <strong>${esc(info.label)}</strong>
          <span>${esc(info.hint)}</span>
        </span>`;
            btn.addEventListener('click', () => seleccionarMetodoMfa(metodo, btn));
            lista.appendChild(btn);
        });
    }

    async function seleccionarMetodoMfa(metodo, btnEl) {
        document.querySelectorAll('.method-option').forEach((b) => b.classList.remove('is-selected'));
        btnEl.classList.add('is-selected');
        estado.metodoSeleccionado = metodo;
        ocultarError('view-mfa-metodo');

        const bloque = document.getElementById('mfa-code-block');
        const info = document.getElementById('mfa-code-info');

        if (metodo === 'Authenticator') {
            info.textContent = METODOS.Authenticator.hint;
            bloque.classList.remove('hidden');
            document.getElementById('mfa-codigo').focus();
            return;
        }

        btnEl.disabled = true;
        try {
            const data = await apiCall('/auth/enviar-codigo-mfa', {
                body: { mfaToken: estado.mfaToken, metodo },
            });
            info.textContent = (data && data.mensaje) || 'Código enviado.';
            bloque.classList.remove('hidden');
            document.getElementById('mfa-codigo').focus();
        } catch (err) {
            mostrarError('view-mfa-metodo', err.message);
        } finally {
            btnEl.disabled = false;
        }
    }

    document.getElementById('mfa-reenviar').addEventListener('click', () => {
        if (!estado.metodoSeleccionado || estado.metodoSeleccionado === 'Authenticator') return;
        const btn = document.querySelector(`.method-option[data-metodo="${estado.metodoSeleccionado}"]`);
        if (btn) seleccionarMetodoMfa(estado.metodoSeleccionado, btn);
    });

    document.getElementById('form-mfa-code').addEventListener('submit', (e) => {
        e.preventDefault();
        const codigo = document.getElementById('mfa-codigo').value.trim();
        if (!codigo) return;

        conCarga(document.getElementById('mfa-code-submit'), async () => {
            try {
                const data = await apiCall('/auth/verify-mfa', {
                    body: { mfaToken: estado.mfaToken, metodo: estado.metodoSeleccionado, codigo },
                });
                finalizarSesion(data.token, data.refreshToken);
            } catch (err) {
                mostrarError('view-mfa-metodo', err.message);
            }
        })();
    });

    document.getElementById('go-mfa-recovery').addEventListener('click', () => mostrarVista('view-mfa-recovery'));
    document.getElementById('recovery-volver').addEventListener('click', () => mostrarVista('view-mfa-metodo'));
    document.getElementById('mfa-volver-login').addEventListener('click', () => {
        estado.mfaToken = null;
        mostrarVista('view-login');
    });

    // vista: codigo de recuperacion MFA
    document.getElementById('form-mfa-recovery').addEventListener('submit', (e) => {
        e.preventDefault();
        const codigo = document.getElementById('recovery-codigo').value.trim();
        if (!codigo) return;

        conCarga(document.getElementById('recovery-submit'), async () => {
            try {
                const data = await apiCall('/auth/verify-mfa-recovery', {
                    body: { mfaToken: estado.mfaToken, codigoRecuperacion: codigo },
                });
                finalizarSesion(data.token, data.refreshToken);
            } catch (err) {
                mostrarError('view-mfa-recovery', err.message);
            }
        })();
    });

    // vista: olvide mi contrasena
    document.getElementById('go-forgot-password').addEventListener('click', () => mostrarVista('view-forgot'));
    document.getElementById('forgot-volver').addEventListener('click', () => mostrarVista('view-login'));
    document.getElementById('forgot-sent-volver').addEventListener('click', () => mostrarVista('view-login'));

    document.getElementById('form-forgot').addEventListener('submit', (e) => {
        e.preventDefault();
        const email = document.getElementById('forgot-email').value.trim();
        if (!email) return;

        conCarga(document.getElementById('forgot-submit'), async () => {
            try {
                await apiCall('/auth/olvide-password', { body: { email } });
                mostrarVista('view-forgot-sent');
            } catch (err) {
                // el backend no revela si el correo existe, este error es de red o servidor
                mostrarError('view-forgot', err.message);
            }
        })();
    });
})();
