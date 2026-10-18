/* ============================================================================
   PensamientoDelDiaBanco_RM — AMPLIACIÓN (2026-09-03)
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Pedido explícito del usuario: "esos pensamientos ¿podríamos actualizarlo de
   manera dinámica para que no se repitan? por lo menos para tener una cantidad
   considerada". El banco original (SQL/PensamientoDelDia_RM.sql) sembró 49
   frases — con eso el ciclo se repetía cada 49 días (~7.4 veces al año). Este
   script AGREGA ~70 frases más (banco final ≈120) sin duplicar las que ya
   están cargadas: cada frase se inserta solo si su texto exacto NO existe
   todavía en la tabla (WHERE NOT EXISTS), así que es seguro volver a
   ejecutarlo si hiciera falta.

   De la mano va un cambio en el código (Controllers/ApiPdfPresController.cs,
   ObtenerPensamientoDelDia): la selección del día pasó de "día del año MOD
   total" (ciclo mecánico y corto) a un barajado determinístico por AÑO (cada
   año usa un orden distinto, pseudoaleatorio pero reproducible) — con ~120
   frases, el ciclo completo ahora tarda ~120 días en vez de 49.

   Usuario "Ventas" es solo lectura sobre la BD; ya tiene permisos DML sobre
   esta tabla desde el script original (SQL/PensamientoDelDia_RM.sql). Este
   script solo agrega filas, no toca el esquema — no hace falta GRANT de nuevo.
   ============================================================================ */

IF OBJECT_ID('dbo.PensamientoDelDiaBanco_RM','U') IS NULL
BEGIN
    RAISERROR('PensamientoDelDiaBanco_RM no existe todavía — correr primero SQL/PensamientoDelDia_RM.sql', 16, 1);
    RETURN;
END
GO

;WITH Nuevas(Frase, Autor) AS (
    SELECT * FROM (VALUES
    (N'El que no arriesga, no gana.', N'Refrán popular'),
    (N'La venta se cierra con los oídos, no con la boca.', N'Anónimo'),
    (N'Un cliente bien atendido regresa; uno mal atendido se lo cuenta a diez.', N'Anónimo'),
    (N'La preparación de hoy determina los resultados de mañana.', N'Anónimo'),
    (N'No hay ruta fácil, hay rutas bien trabajadas.', N'Anónimo'),
    (N'El entusiasmo es contagioso — empieza por ti.', N'Anónimo'),
    (N'La rutina bien hecha es la base de la excelencia.', N'Anónimo'),
    (N'Cada objeción es una pregunta que el cliente todavía no supo hacer.', N'Anónimo'),
    (N'El tiempo que inviertes en planear, lo ahorras en corregir.', N'Anónimo'),
    (N'Un equipo unido supera cualquier meta individual.', N'Anónimo'),
    (N'La honestidad con el cliente es la venta más rentable a largo plazo.', N'Anónimo'),
    (N'No hay segunda oportunidad para una primera impresión.', N'Anónimo'),
    (N'El que se prepara la noche anterior, gana la mañana siguiente.', N'Anónimo'),
    (N'La venta no termina cuando el cliente dice sí; empieza ahí.', N'Anónimo'),
    (N'Ser puntual es una forma silenciosa de decir "me importas".', N'Anónimo'),
    (N'La energía que llevas a la primera visita se nota en todas las demás.', N'Anónimo'),
    (N'Un "no" de hoy puede ser un "sí" bien trabajado de mañana.', N'Anónimo'),
    (N'El detalle que nadie pide es el que hace la diferencia.', N'Anónimo'),
    (N'La ruta se camina un cliente a la vez.', N'Anónimo'),
    (N'Nadie recuerda cuánto vendiste un mal día; recuerdan cómo trataste a la gente.', N'Anónimo'),
    (N'La superación personal empieza por superar el día de ayer.', N'Anónimo'),
    (N'El éxito no es un accidente, es trabajo, perseverancia y aprendizaje.', N'Pelé'),
    (N'Solo se pierde lo que se deja de intentar.', N'Anónimo'),
    (N'El liderazgo se demuestra con el ejemplo, no con el discurso.', N'Anónimo'),
    (N'La actitud determina la altitud.', N'Zig Ziglar'),
    (N'Nadie construye una gran empresa solo; se construye en equipo.', N'Anónimo'),
    (N'El éxito es ir de fracaso en fracaso sin perder el entusiasmo.', N'Winston Churchill'),
    (N'Cuida los pequeños gastos; un pequeño agujero hunde un gran barco.', N'Benjamin Franklin'),
    (N'No cuentes los productos que vendiste; cuenta los clientes que confiaron en ti.', N'Anónimo'),
    (N'La calidad nunca es un accidente; siempre es el resultado de un esfuerzo inteligente.', N'John Ruskin'),
    (N'Haz lo que puedas, con lo que tengas, donde estés.', N'Theodore Roosevelt'),
    (N'El fracaso es una oportunidad para comenzar de nuevo con más inteligencia.', N'Henry Ford'),
    (N'Ganar no lo es todo, pero querer ganar sí.', N'Vince Lombardi'),
    (N'La mejor forma de predecir el futuro es crearlo.', N'Peter Drucker'),
    (N'Un objetivo sin un plan es solo un deseo.', N'Antoine de Saint-Exupéry'),
    (N'El trabajo duro vence al talento cuando el talento no trabaja duro.', N'Anónimo'),
    (N'Lo que se mide, se puede mejorar.', N'Peter Drucker'),
    (N'La disciplina pesa gramos; el arrepentimiento pesa toneladas.', N'Anónimo'),
    (N'Nadie llega tarde a una cita que le importa.', N'Anónimo'),
    (N'La confianza del cliente no se compra, se gana visita a visita.', N'Anónimo'),
    (N'El mejor discurso de ventas es un cliente satisfecho contándolo.', N'Anónimo'),
    (N'Cada mañana trae nuevas fuerzas y nuevos pensamientos.', N'Eleanor Roosevelt'),
    (N'La grandeza no está en nunca caer, sino en levantarse cada vez.', N'Confucio'),
    (N'El único límite a nuestros logros de mañana son las dudas de hoy.', N'Franklin D. Roosevelt'),
    (N'No esperes el momento perfecto; toma el momento y hazlo perfecto.', N'Anónimo'),
    (N'La venta empieza donde termina la excusa.', N'Anónimo'),
    (N'Ser mejor que ayer, esa es la única competencia que importa.', N'Anónimo'),
    (N'El servicio al cliente no es un departamento, es una actitud.', N'Anónimo'),
    (N'Un buen plan hoy es mejor que un plan perfecto mañana.', N'George S. Patton'),
    (N'Las oportunidades no se pierden; las toma otro.', N'Anónimo'),
    (N'Sonreír cuesta lo mismo que fruncir el ceño y vende más.', N'Anónimo'),
    (N'El éxito en equipo se mide por el vendedor con más dificultades, no por el mejor.', N'Anónimo'),
    (N'La ruta más corta al "sí" pasa por escuchar de verdad.', N'Anónimo'),
    (N'Trata a cada cliente como si fuera el único que tienes.', N'Anónimo'),
    (N'Los grandes logros requieren tiempo.', N'Anónimo'),
    (N'No hay viento favorable para el que no sabe a qué puerto se dirige.', N'Séneca'),
    (N'El que quiere, busca el modo; el que no quiere, busca la excusa.', N'Refrán popular'),
    (N'Cada cierre exitoso empezó con una apertura honesta.', N'Anónimo'),
    (N'La cortesía no cuesta nada y lo compra todo.', N'Mary Wortley Montagu'),
    (N'Un equipo motivado mueve montañas de presupuesto.', N'Anónimo'),
    (N'Se aprende más de un cliente difícil que de diez fáciles.', N'Anónimo'),
    (N'La constancia transforma lo ordinario en extraordinario.', N'Anónimo'),
    (N'El primer paso siempre es el más importante.', N'Anónimo'),
    (N'Hazlo con pasión o no lo hagas.', N'Anónimo'),
    (N'La reputación se construye en años y se pierde en minutos — cuídala en cada visita.', N'Anónimo'),
    (N'El buen vendedor no vende un producto, resuelve un problema.', N'Anónimo'),
    (N'Cuando algo es importante, lo haces aunque no sea fácil.', N'Elon Musk'),
    (N'La mejor inversión es la que haces en ti mismo.', N'Warren Buffett'),
    (N'No hay atajo que valga más que el camino bien recorrido.', N'Anónimo'),
    (N'Un cliente recordado por su nombre se siente en casa.', N'Anónimo'),
    (N'El equipo que se comunica bien, vende mejor.', N'Anónimo'),
    (N'Grandes ventas empiezan con pequeños detalles.', N'Anónimo'),
    (N'La actitud de servicio abre más puertas que cualquier catálogo.', N'Anónimo')
    ) AS v(Frase, Autor)
)
INSERT INTO dbo.PensamientoDelDiaBanco_RM (Frase, Autor)
SELECT n.Frase, n.Autor
FROM Nuevas n
WHERE NOT EXISTS (SELECT 1 FROM dbo.PensamientoDelDiaBanco_RM b WHERE b.Frase = n.Frase);
GO

/* Revision:
   SELECT COUNT(*) TotalFrases FROM dbo.PensamientoDelDiaBanco_RM;
*/
