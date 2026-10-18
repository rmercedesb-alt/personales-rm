using System.ComponentModel.DataAnnotations;

namespace PorfiAcademy.Data;

// Todas las fechas se guardan en UTC.

public class Plan
{
    public int Id { get; set; }
    [Required, MaxLength(80)] public string Name { get; set; } = "";
    [MaxLength(300)] public string? Description { get; set; }
    public int DurationDays { get; set; }
    /// <summary>Precio en pesos (RD$). Null = «por definir».</summary>
    public decimal? Price { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public enum SubscriptionSource { Admin = 0, Pin = 1, Deposit = 2, Card = 3 }

/// <summary>Un período de acceso de pago. El acceso vigente es el de mayor ExpiresAt entre las suscripciones activas.</summary>
public class Subscription
{
    public int Id { get; set; }
    [Required] public string UserId { get; set; } = "";
    public int PlanId { get; set; }
    public Plan? Plan { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public SubscriptionSource Source { get; set; }
    public bool Revoked { get; set; }
    [MaxLength(300)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(256)] public string? CreatedBy { get; set; }
}

/// <summary>PIN que el dueño entrega al cliente después de recibir su pago.</summary>
public class AccessCode
{
    public int Id { get; set; }
    /// <summary>Normalizado: mayúsculas, sin guiones ni espacios.</summary>
    [Required, MaxLength(32)] public string Code { get; set; } = "";
    public int PlanId { get; set; }
    public Plan? Plan { get; set; }
    /// <summary>Si es null se usa la duración del plan.</summary>
    public int? DurationDays { get; set; }
    [MaxLength(200)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
    public string? RedeemedByUserId { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public bool Cancelled { get; set; }
}

public enum RequestType { Info = 0, FreeTraining = 1, Subscription = 2, Deposit = 3 }
public enum RequestStatus { New = 0, Contacted = 1, Approved = 2, Rejected = 3 }

/// <summary>Solicitud de un visitante o usuario: información, entrenamiento gratis, suscripción o aviso de depósito.</summary>
public class SubscriptionRequest
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(256)] public string? Email { get; set; }
    public RequestType Type { get; set; }
    public int? PlanId { get; set; }
    public Plan? Plan { get; set; }
    [MaxLength(1000)] public string? Message { get; set; }
    /// <summary>Número de referencia/comprobante del depósito.</summary>
    [MaxLength(120)] public string? DepositReference { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? HandledAt { get; set; }
    [MaxLength(256)] public string? HandledBy { get; set; }
    [MaxLength(500)] public string? AdminNote { get; set; }
}

public enum SlideKind { Image = 0, Video = 1 }

/// <summary>Diapositiva del carrusel promocional de la portada (rota cada 10 s).</summary>
public class PromoSlide
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Title { get; set; } = "";
    [MaxLength(300)] public string? Subtitle { get; set; }
    public SlideKind Kind { get; set; }
    /// <summary>Nombre del archivo de la imagen en la carpeta de datos (solo nombre, nunca una ruta).</summary>
    [MaxLength(80)] public string? ImageFile { get; set; }
    /// <summary>Clip promocional público de Mux (tipo Video). Es un corte corto, nunca el video de pago.</summary>
    [MaxLength(80)] public string? MuxPlaybackId { get; set; }
    [MaxLength(100)] public string? CtaText { get; set; }
    /// <summary>Destino del botón; por defecto lleva a suscribirse.</summary>
    [MaxLength(300)] public string? CtaUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int Views { get; set; }
    public int Clicks { get; set; }
}

public class SiteSetting
{
    [Key, MaxLength(60)] public string Key { get; set; } = "";
    [MaxLength(2000)] public string Value { get; set; } = "";
}

/// <summary>Datos propios de cada video de Mux (si es gratis o de pago).</summary>
public class VideoMeta
{
    [Key, MaxLength(100)] public string AssetId { get; set; } = "";
    public bool IsFree { get; set; }
}
