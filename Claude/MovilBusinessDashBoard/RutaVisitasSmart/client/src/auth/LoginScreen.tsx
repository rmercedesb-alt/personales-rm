import { useEffect, useState } from "react";
import { fetchEmpresa, login } from "./api";

interface Props {
  onLoggedIn: () => void;
}

/**
 * Pantalla de acceso — usuario/contraseña contra `UsuarioSistema` (misma
 * tabla que usan los dashboards .NET hermanos). El logo y el nombre de marca
 * salen de la tabla `Empresa`; si no hay conexión, NO se muestra ningún logo
 * ni nombre de respaldo (a propósito — mostrar una marca sin poder confirmar
 * la conexión real sería engañoso).
 */
export function LoginScreen({ onLoggedIn }: Props) {
  const [usuario, setUsuario] = useState("");
  const [clave, setClave] = useState("");
  const [verClave, setVerClave] = useState(false);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [nombreEmpresa, setNombreEmpresa] = useState<string | null>(null);
  const [logoOk, setLogoOk] = useState(true);

  useEffect(() => {
    fetchEmpresa().then((info) => {
      if (info.ok && info.nombre) setNombreEmpresa(info.nombre);
    });
  }, []);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!usuario.trim() || !clave) return;
    setCargando(true);
    setError(null);
    try {
      const resultado = await login(usuario.trim(), clave);
      if (resultado.ok) {
        onLoggedIn();
      } else {
        setError(resultado.error ?? "No se pudo iniciar sesión");
        setCargando(false);
      }
    } catch {
      setError("No se pudo conectar con el servidor");
      setCargando(false);
    }
  }

  return (
    <div className="login-shell">
      <div className="login-brand">
        {logoOk && <img src="/api/logo" alt="" className="login-logo" onError={() => setLogoOk(false)} />}
        <div>
          <div className="login-brand-name">{nombreEmpresa ?? "RutaVisitasSmart"}</div>
          <div className="login-brand-sub">Mantenimiento y visualización de rutas</div>
        </div>
      </div>

      <div className="login-card">
        <h1>Iniciar sesión</h1>
        <p>Ingresa tus credenciales del sistema para continuar</p>

        <form onSubmit={handleSubmit}>
          <label className="login-field">
            <span>Usuario</span>
            <input
              type="text"
              value={usuario}
              onChange={(e) => setUsuario(e.target.value)}
              placeholder="Tu usuario del sistema"
              autoComplete="username"
              autoFocus
              required
            />
          </label>
          <label className="login-field">
            <span>Contraseña</span>
            <div className="login-pw-wrap">
              <input
                type={verClave ? "text" : "password"}
                value={clave}
                onChange={(e) => setClave(e.target.value)}
                placeholder="Contraseña"
                autoComplete="current-password"
                required
              />
              <button type="button" className="login-pw-eye" onClick={() => setVerClave((v) => !v)} tabIndex={-1}>
                👁
              </button>
            </div>
          </label>
          <button className="login-btn" type="submit" disabled={cargando}>
            {cargando ? "Verificando…" : "Entrar"}
          </button>
        </form>

        {error && <div className="login-msg login-msg-err">{error}</div>}
      </div>

      <div className="login-footer">{nombreEmpresa ? `${nombreEmpresa} — ` : ""}Acceso restringido a usuarios autorizados</div>
    </div>
  );
}
