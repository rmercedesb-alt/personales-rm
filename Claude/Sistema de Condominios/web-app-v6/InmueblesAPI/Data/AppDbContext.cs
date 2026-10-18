using InmueblesAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace InmueblesAPI.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Propietario>      Propietarios     { get; set; }
    public DbSet<CoPropietario>    CoPropietarios   { get; set; }
    public DbSet<Inmueble>         Inmuebles        { get; set; }
    public DbSet<Inquilino>        Inquilinos       { get; set; }
    public DbSet<Contrato>         Contratos        { get; set; }
    public DbSet<CxCCargo>         CxCCargos        { get; set; }
    public DbSet<CxCPago>          CxCPagos         { get; set; }
    public DbSet<AbonoCuenta>      AbonosCuenta     { get; set; }
    public DbSet<SesionCaja>       SesionesCaja     { get; set; }
    public DbSet<Liquidacion>      Liquidaciones    { get; set; }
    public DbSet<LogCustodia>      LogCustodia      { get; set; }
    public DbSet<Adelanto>         Adelantos        { get; set; }
    public DbSet<AdelantoDocumento> AdelantoDocumentos { get; set; }
    public DbSet<UsuarioSistema>   Usuarios         { get; set; }
    public DbSet<ConfigSistema>    Configuracion    { get; set; }
    public DbSet<Empresa>          Empresas         { get; set; }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Claves primarias (todas las entidades usan string Id)
        mb.Entity<Propietario>().HasKey(e => e.Id);
        mb.Entity<CoPropietario>().HasKey(e => e.Id);
        mb.Entity<Inmueble>().HasKey(e => e.Id);
        mb.Entity<Inquilino>().HasKey(e => e.Id);
        mb.Entity<Contrato>().HasKey(e => e.Id);
        mb.Entity<CxCCargo>().HasKey(e => e.Id);
        mb.Entity<CxCPago>().HasKey(e => e.Id);
        mb.Entity<AbonoCuenta>().HasKey(e => e.Id);
        mb.Entity<SesionCaja>().HasKey(e => e.Id);
        mb.Entity<Liquidacion>().HasKey(e => e.Id);
        mb.Entity<LogCustodia>().HasKey(e => e.Id);
        mb.Entity<Adelanto>().HasKey(e => e.Id);
        mb.Entity<AdelantoDocumento>().HasKey(e => e.Id);
        mb.Entity<UsuarioSistema>().HasKey(e => e.Email);
        mb.Entity<ConfigSistema>().HasKey(e => e.Clave);
        mb.Entity<Empresa>().HasKey(e => e.Id);

        // Índices para consultas frecuentes
        mb.Entity<CoPropietario>().HasIndex(e => e.IdPropietario);
        mb.Entity<Contrato>().HasIndex(e => e.IdInmueble);
        mb.Entity<Contrato>().HasIndex(e => e.IdInquilino);
        mb.Entity<CxCCargo>().HasIndex(e => e.IdContrato);
        mb.Entity<CxCCargo>().HasIndex(e => e.IdInmueble);
        mb.Entity<CxCCargo>().HasIndex(e => e.Estado);
        mb.Entity<CxCPago>().HasIndex(e => e.IdCxC);
        mb.Entity<CxCPago>().HasIndex(e => e.IdInmueble);
        mb.Entity<AbonoCuenta>().HasIndex(e => e.IdInquilino);
        mb.Entity<Adelanto>().HasIndex(e => e.IdPropietario);
        mb.Entity<AdelantoDocumento>().HasIndex(e => e.IdAdelanto);
        mb.Entity<Liquidacion>().HasIndex(e => e.IdPropietario);

        // Nombres de tablas en snake_case (convención PostgreSQL)
        mb.Entity<Propietario>().ToTable("propietarios");
        mb.Entity<CoPropietario>().ToTable("co_propietarios");
        mb.Entity<Inmueble>().ToTable("inmuebles");
        mb.Entity<Inquilino>().ToTable("inquilinos");
        mb.Entity<Contrato>().ToTable("contratos");
        mb.Entity<CxCCargo>().ToTable("cxc_cargos");
        mb.Entity<CxCPago>().ToTable("cxc_pagos");
        mb.Entity<AbonoCuenta>().ToTable("abonos_cuenta");
        mb.Entity<SesionCaja>().ToTable("sesiones_caja");
        mb.Entity<Liquidacion>().ToTable("liquidaciones");
        mb.Entity<LogCustodia>().ToTable("log_custodia");
        mb.Entity<Adelanto>().ToTable("adelantos");
        mb.Entity<AdelantoDocumento>().ToTable("adelanto_documentos");
        mb.Entity<UsuarioSistema>().ToTable("usuarios");
        mb.Entity<ConfigSistema>().ToTable("configuracion");
        mb.Entity<Empresa>().ToTable("empresa");
    }
}
