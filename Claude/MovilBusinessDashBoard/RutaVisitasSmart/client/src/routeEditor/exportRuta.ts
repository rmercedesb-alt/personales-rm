import { DIAS_SEMANA, type RutaPorDia, type Vendedor } from "../types";

export async function exportarRuta(vendedor: Vendedor, ruta: RutaPorDia): Promise<void> {
  const dias: Record<string, Array<{ cliId: number; cliCodigo: string | null; cliNombre: string | null; semanasActivas: number[] }>> = {};
  for (const dia of DIAS_SEMANA) {
    dias[dia] = ruta[dia].map((c) => ({
      cliId: c.cliId,
      cliCodigo: c.cliCodigo,
      cliNombre: c.cliNombre,
      semanasActivas: c.semanasActivas,
    }));
  }

  const res = await fetch("/api/export-ruta", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ repCodigo: vendedor.repCodigo, repNombre: vendedor.repNombre, dias }),
  });
  if (!res.ok) throw new Error(`Error ${res.status} generando el export`);

  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `Ruta_${vendedor.repCodigo}.xlsx`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
