/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoUmbralesRD_Seed (2026-09-01) — 2 variables NUEVAS dentro del
   catálogo genérico YA EXISTENTE PresupuestoVariables_RM/Valores_RM, NO una
   tabla nueva. Pedido explícito del usuario: "si el presupuesto propuesto
   de un suplidor es 100000 y quiero que el mínimo sea 1500000, ¿dónde
   coloco esto? ¿son mensuales? ¿existe por categorías? ¿puede ser para
   arriba o para abajo?".

   UMBRAL_MINIMO_RD / UMBRAL_MAXIMO_RD: un monto ABSOLUTO en RD$ (no un %,
   a diferencia de PISO_MINIMO_PCT) que el motor de Generar (PASO 1d,
   ApiPresupuestoGestionController.cs) compara contra la suma TOTAL
   propuesta de ese Suplidor o Categoría (sumando TODOS los vendedores),
   convertida a RD$ con el mismo precio de Lista real que usa "Sugerido"
   en el resto del módulo. Si queda por debajo del mínimo o por encima del
   máximo, se reparte proporcionalmente entre los SKU/vendedores de ese
   grupo (mismo mecanismo ya usado para Activación).

   Al ser parte del catálogo genérico, YA hereda gratis:
   - Ámbito: Suplidor (Cat3ID) o Categoría (Cat1ID) — ambos "sí" a la
     pregunta "¿existe por categorías?". (Global se ignora a propósito en
     este hook — ver comentario en el controller.)
   - Período: Mes / Trimestre / Año (cascada, más específico gana) — "sí"
     a la pregunta "¿son mensuales?" (pueden ser mensuales, o un valor que
     aplique a todo el año si no se especifica mes).
   - Mínimo Y Máximo por separado — "sí" a la pregunta "¿para arriba o
     para abajo?": ambos, cada uno independiente.

   Se editan con la MISMA UI de "Configuración de variables" que ya existe
   (tabla de Definiciones + panel de Valores) — no hace falta pantalla
   nueva. Este script solo da de alta las 2 DEFINICIONES; los VALORES
   (montos concretos por Suplidor/Categoría/período) se cargan desde ahí.

   Ejecutar en MovilBusiness5STD (producción).
   ═══════════════════════════════════════════════════════════════════════ */

IF NOT EXISTS (SELECT 1 FROM PresupuestoVariables_RM WHERE VarCodigo = 'UMBRAL_MINIMO_RD')
BEGIN
    INSERT INTO PresupuestoVariables_RM(VarCodigo, VarNombre, VarDescripcion, VarTipoDato, VarAmbitoPermitido, Activo)
    VALUES('UMBRAL_MINIMO_RD', 'Umbral mínimo (RD$)',
           'Monto ABSOLUTO en RD$ por debajo del cual no debe quedar el presupuesto propuesto TOTAL de este Suplidor o Categoría (sumando todos los vendedores). Se reparte proporcionalmente si hace falta subirlo.',
           'decimal', 'Suplidor,Categoria', 1);
END
GO
IF NOT EXISTS (SELECT 1 FROM PresupuestoVariables_RM WHERE VarCodigo = 'UMBRAL_MAXIMO_RD')
BEGIN
    INSERT INTO PresupuestoVariables_RM(VarCodigo, VarNombre, VarDescripcion, VarTipoDato, VarAmbitoPermitido, Activo)
    VALUES('UMBRAL_MAXIMO_RD', 'Umbral máximo (RD$)',
           'Monto ABSOLUTO en RD$ por encima del cual no debe quedar el presupuesto propuesto TOTAL de este Suplidor o Categoría (sumando todos los vendedores). Se reparte proporcionalmente si hace falta bajarlo.',
           'decimal', 'Suplidor,Categoria', 1);
END
GO
