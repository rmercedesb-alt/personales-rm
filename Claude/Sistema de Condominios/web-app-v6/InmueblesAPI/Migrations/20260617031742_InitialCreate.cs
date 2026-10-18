using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InmueblesAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "abonos_cuenta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdInquilino = table.Column<string>(type: "text", nullable: false),
                    NombreInquilino = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MontoOriginal = table.Column<decimal>(type: "numeric", nullable: false),
                    SaldoDisponible = table.Column<decimal>(type: "numeric", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    IdReciboOrigen = table.Column<string>(type: "text", nullable: false),
                    Usuario = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_abonos_cuenta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "adelantos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdPropietario = table.Column<string>(type: "text", nullable: false),
                    NombrePropietario = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Concepto = table.Column<string>(type: "text", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    IdLiquidacion = table.Column<string>(type: "text", nullable: false),
                    Usuario = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adelantos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "co_propietarios",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdPropietario = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Cedula = table.Column<string>(type: "text", nullable: false),
                    Telefono = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Banco = table.Column<string>(type: "text", nullable: false),
                    NumeroCuenta = table.Column<string>(type: "text", nullable: false),
                    TipoCuenta = table.Column<string>(type: "text", nullable: false),
                    PesoPorc = table.Column<decimal>(type: "numeric", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_co_propietarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "configuracion",
                columns: table => new
                {
                    Clave = table.Column<string>(type: "text", nullable: false),
                    Valor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuracion", x => x.Clave);
                });

            migrationBuilder.CreateTable(
                name: "contratos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdInmueble = table.Column<string>(type: "text", nullable: false),
                    NombreInmueble = table.Column<string>(type: "text", nullable: false),
                    IdInquilino = table.Column<string>(type: "text", nullable: false),
                    NombreInquilino = table.Column<string>(type: "text", nullable: false),
                    IdPropietario = table.Column<string>(type: "text", nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MontoAlquiler = table.Column<decimal>(type: "numeric", nullable: false),
                    Periodicidad = table.Column<string>(type: "text", nullable: false),
                    DiaCobro = table.Column<int>(type: "integer", nullable: false),
                    DiasGracia = table.Column<int>(type: "integer", nullable: false),
                    TipoMora = table.Column<string>(type: "text", nullable: false),
                    MoraPorcentaje = table.Column<decimal>(type: "numeric", nullable: false),
                    MontoMoraFija = table.Column<decimal>(type: "numeric", nullable: false),
                    Deposito = table.Column<decimal>(type: "numeric", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contratos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cxc_cargos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdContrato = table.Column<string>(type: "text", nullable: false),
                    IdInquilino = table.Column<string>(type: "text", nullable: false),
                    NombreInquilino = table.Column<string>(type: "text", nullable: false),
                    IdInmueble = table.Column<string>(type: "text", nullable: false),
                    TipoCargo = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    PeriodoInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodoFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaVencimiento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MontoOriginal = table.Column<decimal>(type: "numeric", nullable: false),
                    MontoMora = table.Column<decimal>(type: "numeric", nullable: false),
                    MontoPagado = table.Column<decimal>(type: "numeric", nullable: false),
                    SaldoPendiente = table.Column<decimal>(type: "numeric", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    DiasMora = table.Column<int>(type: "integer", nullable: false),
                    EnGracia = table.Column<bool>(type: "boolean", nullable: false),
                    FechaUltimoPago = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaUltimaActualizacionMora = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cxc_cargos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cxc_pagos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdCxC = table.Column<string>(type: "text", nullable: false),
                    IdContrato = table.Column<string>(type: "text", nullable: false),
                    IdInquilino = table.Column<string>(type: "text", nullable: false),
                    NombreInquilino = table.Column<string>(type: "text", nullable: false),
                    IdInmueble = table.Column<string>(type: "text", nullable: false),
                    IdSesionCaja = table.Column<string>(type: "text", nullable: false),
                    FechaPago = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MontoAlquiler = table.Column<decimal>(type: "numeric", nullable: false),
                    MontoMora = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPagado = table.Column<decimal>(type: "numeric", nullable: false),
                    FormaPago = table.Column<string>(type: "text", nullable: false),
                    Referencia = table.Column<string>(type: "text", nullable: false),
                    EstadoCustodia = table.Column<string>(type: "text", nullable: false),
                    IdLiquidacion = table.Column<string>(type: "text", nullable: true),
                    UrlReciboPDF = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdRecibo = table.Column<string>(type: "text", nullable: false),
                    Anulado = table.Column<bool>(type: "boolean", nullable: false),
                    MotivoAnulacion = table.Column<string>(type: "text", nullable: false),
                    MontoAbonoAplicado = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cxc_pagos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inmuebles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdPropietario = table.Column<string>(type: "text", nullable: false),
                    NombrePropietario = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Direccion = table.Column<string>(type: "text", nullable: false),
                    Sector = table.Column<string>(type: "text", nullable: false),
                    Ciudad = table.Column<string>(type: "text", nullable: false),
                    Latitud = table.Column<decimal>(type: "numeric", nullable: false),
                    Longitud = table.Column<decimal>(type: "numeric", nullable: false),
                    MontoAlquiler = table.Column<decimal>(type: "numeric", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    NivelMorosidad = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inmuebles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inquilinos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Cedula = table.Column<string>(type: "text", nullable: false),
                    Telefono = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Ocupacion = table.Column<string>(type: "text", nullable: false),
                    ReferenciaPersonal = table.Column<string>(type: "text", nullable: false),
                    TelefonoReferencia = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inquilinos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "liquidaciones",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdPropietario = table.Column<string>(type: "text", nullable: false),
                    NombrePropietario = table.Column<string>(type: "text", nullable: false),
                    FechaDesde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaHasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalCobrado = table.Column<decimal>(type: "numeric", nullable: false),
                    Comision = table.Column<decimal>(type: "numeric", nullable: false),
                    NetoPropietario = table.Column<decimal>(type: "numeric", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    FechaPago = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenciaPago = table.Column<string>(type: "text", nullable: false),
                    UrlPDF = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_liquidaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "log_custodia",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdPago = table.Column<string>(type: "text", nullable: false),
                    IdContrato = table.Column<string>(type: "text", nullable: false),
                    IdInquilino = table.Column<string>(type: "text", nullable: false),
                    NombreInquilino = table.Column<string>(type: "text", nullable: false),
                    IdInmueble = table.Column<string>(type: "text", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric", nullable: false),
                    EstadoAnterior = table.Column<string>(type: "text", nullable: false),
                    EstadoNuevo = table.Column<string>(type: "text", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: false),
                    Usuario = table.Column<string>(type: "text", nullable: false),
                    FechaMovimiento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HashSHA256 = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_custodia", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "propietarios",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Cedula = table.Column<string>(type: "text", nullable: false),
                    Telefono = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Banco = table.Column<string>(type: "text", nullable: false),
                    NumeroCuenta = table.Column<string>(type: "text", nullable: false),
                    TipoCuenta = table.Column<string>(type: "text", nullable: false),
                    TipoComision = table.Column<string>(type: "text", nullable: false),
                    ComisionPorcentaje = table.Column<decimal>(type: "numeric", nullable: false),
                    ComisionFija = table.Column<decimal>(type: "numeric", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TipoDueno = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_propietarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sesiones_caja",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    TotalEfectivo = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalTransferencia = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalTarjeta = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalCheque = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalGeneral = table.Column<decimal>(type: "numeric", nullable: false),
                    CantidadTransacciones = table.Column<int>(type: "integer", nullable: false),
                    UsuarioApertura = table.Column<string>(type: "text", nullable: true),
                    UsuarioCierre = table.Column<string>(type: "text", nullable: true),
                    FechaCierre = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UrlCierrePDF = table.Column<string>(type: "text", nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sesiones_caja", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    Email = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Rol = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.Email);
                });

            migrationBuilder.CreateIndex(
                name: "IX_abonos_cuenta_IdInquilino",
                table: "abonos_cuenta",
                column: "IdInquilino");

            migrationBuilder.CreateIndex(
                name: "IX_adelantos_IdPropietario",
                table: "adelantos",
                column: "IdPropietario");

            migrationBuilder.CreateIndex(
                name: "IX_co_propietarios_IdPropietario",
                table: "co_propietarios",
                column: "IdPropietario");

            migrationBuilder.CreateIndex(
                name: "IX_contratos_IdInmueble",
                table: "contratos",
                column: "IdInmueble");

            migrationBuilder.CreateIndex(
                name: "IX_contratos_IdInquilino",
                table: "contratos",
                column: "IdInquilino");

            migrationBuilder.CreateIndex(
                name: "IX_cxc_cargos_Estado",
                table: "cxc_cargos",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_cxc_cargos_IdContrato",
                table: "cxc_cargos",
                column: "IdContrato");

            migrationBuilder.CreateIndex(
                name: "IX_cxc_cargos_IdInmueble",
                table: "cxc_cargos",
                column: "IdInmueble");

            migrationBuilder.CreateIndex(
                name: "IX_cxc_pagos_IdCxC",
                table: "cxc_pagos",
                column: "IdCxC");

            migrationBuilder.CreateIndex(
                name: "IX_cxc_pagos_IdInmueble",
                table: "cxc_pagos",
                column: "IdInmueble");

            migrationBuilder.CreateIndex(
                name: "IX_liquidaciones_IdPropietario",
                table: "liquidaciones",
                column: "IdPropietario");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abonos_cuenta");

            migrationBuilder.DropTable(
                name: "adelantos");

            migrationBuilder.DropTable(
                name: "co_propietarios");

            migrationBuilder.DropTable(
                name: "configuracion");

            migrationBuilder.DropTable(
                name: "contratos");

            migrationBuilder.DropTable(
                name: "cxc_cargos");

            migrationBuilder.DropTable(
                name: "cxc_pagos");

            migrationBuilder.DropTable(
                name: "inmuebles");

            migrationBuilder.DropTable(
                name: "inquilinos");

            migrationBuilder.DropTable(
                name: "liquidaciones");

            migrationBuilder.DropTable(
                name: "log_custodia");

            migrationBuilder.DropTable(
                name: "propietarios");

            migrationBuilder.DropTable(
                name: "sesiones_caja");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
