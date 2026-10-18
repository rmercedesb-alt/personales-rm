using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InmueblesAPI.Migrations
{
    public partial class LiquidacionV5Fields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "anulado",
                table: "liquidaciones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "motivo_anulacion",
                table: "liquidaciones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_anulacion",
                table: "liquidaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "usuario_anulacion",
                table: "liquidaciones",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Cambiar estado por defecto de PENDIENTE a GENERADA en registros existentes
            migrationBuilder.Sql("UPDATE liquidaciones SET estado = 'GENERADA' WHERE estado = 'PENDIENTE'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "anulado",           table: "liquidaciones");
            migrationBuilder.DropColumn(name: "motivo_anulacion",  table: "liquidaciones");
            migrationBuilder.DropColumn(name: "fecha_anulacion",   table: "liquidaciones");
            migrationBuilder.DropColumn(name: "usuario_anulacion", table: "liquidaciones");
        }
    }
}
