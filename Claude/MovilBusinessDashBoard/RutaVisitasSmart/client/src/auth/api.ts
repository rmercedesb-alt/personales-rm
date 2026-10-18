export interface Sesion {
  usuario: string;
  nombre: string;
}

export interface EmpresaInfo {
  ok: boolean;
  nombre: string;
}

/** null = no hay sesión iniciada; lanza si el servidor no respondió (sin red). */
export async function fetchSesion(): Promise<Sesion | null> {
  const res = await fetch("/api/session", { credentials: "include" });
  if (res.status === 401) return null;
  if (!res.ok) throw new Error(`Error ${res.status} consultando /api/session`);
  const json = await res.json();
  return { usuario: json.usuario, nombre: json.nombre };
}

export async function login(usuario: string, clave: string): Promise<{ ok: boolean; error?: string }> {
  const res = await fetch("/api/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ usuario, clave }),
  });
  if (!res.ok) return { ok: false, error: `Error ${res.status} del servidor` };
  return res.json();
}

export async function logout(): Promise<void> {
  await fetch("/api/logout", { method: "POST", credentials: "include" });
}

/** ok:false si no hay conexión con la base de datos — el front no debe
 * inventar un nombre de empresa en ese caso. */
export async function fetchEmpresa(): Promise<EmpresaInfo> {
  try {
    const res = await fetch("/api/empresa");
    if (!res.ok) return { ok: false, nombre: "" };
    return await res.json();
  } catch {
    return { ok: false, nombre: "" };
  }
}
