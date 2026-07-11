using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LayupPulse.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductionRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RecipeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                PartReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                FinalStatus = table.Column<int>(type: "INTEGER", nullable: false),
                CompletionPercentage = table.Column<double>(type: "REAL", nullable: false),
                AlarmCount = table.Column<int>(type: "INTEGER", nullable: false),
                AverageTemperatureCelsius = table.Column<double>(type: "REAL", nullable: false),
                AveragePressureBar = table.Column<double>(type: "REAL", nullable: false),
                AverageCompactionForceNewtons = table.Column<double>(type: "REAL", nullable: false),
                AverageFeedRateMillimetersPerSecond = table.Column<double>(type: "REAL", nullable: false),
                MinimumProcessHealthPercentage = table.Column<double>(type: "REAL", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductionRuns", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Alarms",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductionRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                Code = table.Column<int>(type: "INTEGER", nullable: false),
                Severity = table.Column<int>(type: "INTEGER", nullable: false),
                Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                RaisedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                AcknowledgedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                ClearedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Alarms", x => x.Id);
                table.ForeignKey(
                    name: "FK_Alarms_ProductionRuns_ProductionRunId",
                    column: x => x.ProductionRunId,
                    principalTable: "ProductionRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "TelemetryAggregates",
            columns: table => new
            {
                ProductionRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                BucketStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                SampleCount = table.Column<int>(type: "INTEGER", nullable: false),
                AverageFeedRateMillimetersPerSecond = table.Column<double>(type: "REAL", nullable: false),
                AverageCompactionForceNewtons = table.Column<double>(type: "REAL", nullable: false),
                AverageHeaterTemperatureCelsius = table.Column<double>(type: "REAL", nullable: false),
                MinimumHeaterTemperatureCelsius = table.Column<double>(type: "REAL", nullable: false),
                MaximumHeaterTemperatureCelsius = table.Column<double>(type: "REAL", nullable: false),
                AverageMaterialPressureBar = table.Column<double>(type: "REAL", nullable: false),
                MinimumMaterialPressureBar = table.Column<double>(type: "REAL", nullable: false),
                MaximumMaterialPressureBar = table.Column<double>(type: "REAL", nullable: false),
                MinimumCompactionForceNewtons = table.Column<double>(type: "REAL", nullable: false),
                MaximumCompactionForceNewtons = table.Column<double>(type: "REAL", nullable: false),
                AverageProcessHealthPercentage = table.Column<double>(type: "REAL", nullable: false),
                MinimumProcessHealthPercentage = table.Column<double>(type: "REAL", nullable: false),
                EndOfBucketCycleProgressPercentage = table.Column<double>(type: "REAL", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TelemetryAggregates", x => new { x.ProductionRunId, x.BucketStartedAtUtc });
                table.ForeignKey(
                    name: "FK_TelemetryAggregates_ProductionRuns_ProductionRunId",
                    column: x => x.ProductionRunId,
                    principalTable: "ProductionRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Alarms_ProductionRunId",
            table: "Alarms",
            column: "ProductionRunId");

        migrationBuilder.CreateIndex(
            name: "IX_Alarms_RaisedAtUtc",
            table: "Alarms",
            column: "RaisedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_ProductionRuns_FinalStatus",
            table: "ProductionRuns",
            column: "FinalStatus");

        migrationBuilder.CreateIndex(
            name: "IX_ProductionRuns_StartedAtUtc",
            table: "ProductionRuns",
            column: "StartedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Alarms");

        migrationBuilder.DropTable(
            name: "TelemetryAggregates");

        migrationBuilder.DropTable(
            name: "ProductionRuns");
    }
}
