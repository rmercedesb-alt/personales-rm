/* ============================================================================
   PensamientoDelDia_RM
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Cierre del reporte "Cierre Diario" (Supervisión Dinámica -> PDF Presentaciones
   -> Cierres). Pedido explícito del usuario 2026-09-03: una frase distinta cada
   día, "vía web", compartida por todos los que vean el reporte ESE día.

   Dos tablas:
     1) PensamientoDelDia_RM       -> UNA fila por fecha calendario, la frase
        que "ganó" ese día (de la fuente externa, o del banco local si la
        fuente externa falló o el servidor no tiene salida a internet). Se
        llena la primera vez que alguien genera el reporte ese día; de ahí en
        adelante se lee esta fila (no se vuelve a golpear la fuente externa).
     2) PensamientoDelDiaBanco_RM  -> banco local de respaldo (semillado abajo),
        rotado por día del año — nunca deja el reporte sin frase, sin importar
        qué pase con la fuente externa.

   Usuario "Ventas" es solo lectura sobre la BD; al final se otorgan permisos DML
   solo sobre estas tablas _RM. Ejecutar una sola vez (re-ejecutable).
   ============================================================================ */

/* ============================================================== *
 * 1) FRASE DEL DÍA (una fila por fecha, cacheada)                *
 * ============================================================== */
IF OBJECT_ID('dbo.PensamientoDelDia_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.PensamientoDelDia_RM
    (
        Fecha            date          NOT NULL CONSTRAINT PK_PensamientoDia_RM PRIMARY KEY,
        Frase            nvarchar(500) NOT NULL,
        Autor            nvarchar(200) NULL,
        Fuente           varchar(100)  NULL,     -- 'web' | 'banco-local'
        FechaRegistro    datetime      NOT NULL CONSTRAINT DF_PensamientoDia_Reg DEFAULT(GETDATE())
    );
END
GO

/* ============================================================== *
 * 2) BANCO LOCAL de respaldo (semilla ~48 frases en español)     *
 * ============================================================== */
IF OBJECT_ID('dbo.PensamientoDelDiaBanco_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.PensamientoDelDiaBanco_RM
    (
        BancoID  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PensamientoBanco_RM PRIMARY KEY,
        Frase    nvarchar(500) NOT NULL,
        Autor    nvarchar(200) NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PensamientoDelDiaBanco_RM)
INSERT INTO dbo.PensamientoDelDiaBanco_RM (Frase, Autor) VALUES
 ('El éxito es la suma de pequeños esfuerzos repetidos día tras día.', 'Robert Collier'),
 ('No cuentes los días, haz que los días cuenten.', 'Muhammad Ali'),
 ('La disciplina es el puente entre las metas y los logros.', 'Jim Rohn'),
 ('Un vendedor exitoso no espera la oportunidad, la crea.', 'Anónimo'),
 ('El cliente no siempre tiene la razón, pero siempre tiene la percepción.', 'Kyle LaMalfa'),
 ('La actitud es una pequeña cosa que hace una gran diferencia.', 'Winston Churchill'),
 ('No se trata de tener tiempo, se trata de hacer tiempo.', 'Anónimo'),
 ('Cada "no" te acerca un paso más al siguiente "sí".', 'Anónimo'),
 ('La venta comienza cuando el cliente dice no.', 'Elmer Wheeler'),
 ('El trabajo en equipo divide el esfuerzo y multiplica el resultado.', 'Anónimo'),
 ('La constancia vence lo que la dicha no alcanza.', 'Miguel de Cervantes'),
 ('No hay atajos para ningún lugar que valga la pena ir.', 'Beverly Sills'),
 ('El que no arriesga, no gana; el que no siembra, no cosecha.', 'Refrán popular'),
 ('La calidad de tu vida depende de la calidad de tus hábitos diarios.', 'Anónimo'),
 ('El liderazgo es la capacidad de traducir la visión en realidad.', 'Warren Bennis'),
 ('Un buen vendedor escucha el doble de lo que habla.', 'Anónimo'),
 ('Las metas son sueños con fecha límite.', 'Napoleon Hill'),
 ('El precio del éxito es el trabajo duro, la dedicación y el esfuerzo constante.', 'Vince Lombardi'),
 ('Hoy es una nueva oportunidad para superar el récord de ayer.', 'Anónimo'),
 ('La confianza se gana en gotas y se pierde en litros.', 'Anónimo'),
 ('No cuentes los clientes que te dicen que no; cuenta los que te dicen que sí.', 'Anónimo'),
 ('El optimismo es el motor que impulsa cada nueva venta.', 'Anónimo'),
 ('Servir bien hoy es la mejor publicidad para mañana.', 'Anónimo'),
 ('La excelencia no es un acto, sino un hábito.', 'Aristóteles'),
 ('Cada ruta que cierras bien es una relación que se abre.', 'Anónimo'),
 ('El que persevera, alcanza.', 'Refrán popular'),
 ('La motivación te pone en marcha; el hábito te mantiene en movimiento.', 'Jim Ryun'),
 ('Un equipo que se apoya, vende más y se cansa menos.', 'Anónimo'),
 ('Vende soluciones, no solo productos.', 'Anónimo'),
 ('La primera venta del día es a ti mismo: cree en lo que ofreces.', 'Anónimo'),
 ('El cliente satisfecho es el mejor vendedor que existe.', 'Anónimo'),
 ('No hay viento favorable para el que no sabe a dónde va.', 'Séneca'),
 ('Empieza donde estás, usa lo que tienes, haz lo que puedas.', 'Arthur Ashe'),
 ('El fracaso es simplemente la oportunidad de empezar de nuevo, con más inteligencia.', 'Henry Ford'),
 ('Sonríe: se nota en la voz y se nota en la venta.', 'Anónimo'),
 ('La puntualidad es la cortesía de los reyes... y de los buenos vendedores.', 'Anónimo'),
 ('Cada visita cuenta, cada cliente importa.', 'Anónimo'),
 ('El orden y la planificación duplican el resultado del esfuerzo diario.', 'Anónimo'),
 ('Grandes cosas nunca vienen de la zona de confort.', 'Anónimo'),
 ('El respeto al cliente se construye en cada entrega a tiempo.', 'Anónimo'),
 ('No busques excusas, busca resultados.', 'Anónimo'),
 ('La actitud correcta convierte un mal día en una buena lección.', 'Anónimo'),
 ('El éxito de hoy es la disciplina de ayer.', 'Anónimo'),
 ('Trabaja en silencio, deja que tus números hablen.', 'Anónimo'),
 ('La confianza del cliente se gana cumpliendo lo prometido.', 'Anónimo'),
 ('Un buen día empieza con una buena actitud, no con un buen clima.', 'Anónimo'),
 ('El mejor momento para vender fue ayer; el segundo mejor es ahora.', 'Anónimo'),
 ('La suma de pequeñas ventas hace grandes cierres de mes.', 'Anónimo'),
 ('Cuida a tu cliente y él cuidará tu negocio.', 'Anónimo');
GO

/* ============================================================== *
 * 3) PERMISOS para la app (usuario "Ventas")                     *
 * ============================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PensamientoDelDia_RM      TO [Ventas];
GRANT SELECT, INSERT             ON dbo.PensamientoDelDiaBanco_RM TO [Ventas];
GO

/* Revision:
   SELECT * FROM dbo.PensamientoDelDia_RM ORDER BY Fecha DESC;
   SELECT COUNT(*) FROM dbo.PensamientoDelDiaBanco_RM;
*/
