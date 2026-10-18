import { useEffect, useState } from "react";
import { fetchSesion, logout, type Sesion } from "./api";
import { LoginScreen } from "./LoginScreen";

interface Props {
  children: (sesion: Sesion, onLogout: () => void) => React.ReactNode;
}

/** Verifica la sesión al cargar la página; muestra el login si no hay una
 * activa, y solo entonces renderiza el resto de la app (vía render-prop, para
 * pasarle los datos de sesión sin que el resto del árbol tenga que volver a
 * pedirlos). */
export function AuthGate({ children }: Props) {
  const [estado, setEstado] = useState<"cargando" | "error" | "sinSesion" | "conSesion">("cargando");
  const [sesion, setSesion] = useState<Sesion | null>(null);

  function revisarSesion() {
    setEstado("cargando");
    fetchSesion()
      .then((s) => {
        if (s) {
          setSesion(s);
          setEstado("conSesion");
        } else {
          setEstado("sinSesion");
        }
      })
      .catch(() => setEstado("error"));
  }

  useEffect(() => {
    revisarSesion();
  }, []);

  async function handleLogout() {
    await logout();
    setSesion(null);
    setEstado("sinSesion");
  }

  if (estado === "cargando") {
    return <div className="login-shell login-cargando">Verificando sesión…</div>;
  }
  if (estado === "error") {
    return (
      <div className="login-shell login-cargando">
        <div>No se pudo conectar con el servidor.</div>
        <button className="login-btn" style={{ marginTop: 16, maxWidth: 200 }} onClick={revisarSesion}>
          Reintentar
        </button>
      </div>
    );
  }
  if (estado === "sinSesion" || !sesion) {
    return <LoginScreen onLoggedIn={revisarSesion} />;
  }
  return <>{children(sesion, handleLogout)}</>;
}
