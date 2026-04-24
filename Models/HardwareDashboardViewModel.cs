using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalTechClientPortal.Models
{
    public sealed class HardwareDashboardViewModel
    {
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public Guid? PropietarioId { get; set; }
        public List<HardwareOwnerOptionVm> Propietarios { get; set; } = new();
        public List<HardwareRecordVm> Registros { get; set; } = new();

        public decimal TotalUtilidad => Registros.Sum(r => r.Utilidad ?? 0m);
        public decimal TotalVenta => Registros.Sum(r => r.PrecioVenta ?? 0m);
        public decimal TotalComision => TotalUtilidad * 0.3m;
        public decimal MargenPromedio
        {
            get
            {
                var valores = Registros
                    .Where(r => r.ValorMargen.HasValue)
                    .Select(r => r.ValorMargen!.Value)
                    .ToList();

                return valores.Count == 0 ? 0m : valores.Average();
            }
        }

        public List<HardwareMonthlySalesVm> VentasMensuales => Registros
            .Where(r => r.CreatedOn.HasValue)
            .GroupBy(r => new DateTime(r.CreatedOn!.Value.Year, r.CreatedOn.Value.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new HardwareMonthlySalesVm
            {
                Mes = g.Key,
                TotalVenta = g.Sum(r => r.PrecioVenta ?? 0m)
            })
            .ToList();
    }

    public sealed class HardwareRecordVm
    {
        public Guid Id { get; set; }
        public Guid? OwnerId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public DateTime? CreatedOn { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public decimal? PrecioVenta { get; set; }
        public decimal? Utilidad { get; set; }
        public decimal? ValorMargen { get; set; }
        public int DiasUltimaModificacion { get; set; }
        public Guid? ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public bool TieneOrdenDeCompra { get; set; }
        public string OrdenDeCompraFileName { get; set; } = string.Empty;
    }

    public sealed class HardwareOwnerOptionVm
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class HardwareMonthlySalesVm
    {
        public DateTime Mes { get; set; }
        public decimal TotalVenta { get; set; }
    }
}
