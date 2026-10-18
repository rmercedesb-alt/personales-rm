namespace ADEnterprise.Models;

public enum PerfilUsuario
{
    Administrador,
    Operativo,
    Consultas
}

public enum EstadoUsuario
{
    Activo,
    Inactivo
}

public enum EstadoFactura
{
    Pendiente,
    PagadoParcial,
    Pagada,
    Vencida,
    Credito          // Abono a cuenta / crédito disponible (saldo negativo)
}

public enum FormaPago
{
    Efectivo,
    Cheque,
    Transferencia,
    Deposito,
    Abono
}

public enum OrigenDato
{
    Sistema,
    Migracion
}
