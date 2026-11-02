using System.ComponentModel.DataAnnotations;

namespace Dana.Models;

public class Product : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string? Sku { get; set; }
    /// <summary>Código CABYS (Catálogo de Bienes y Servicios de Costa Rica), para facturación electrónica.</summary>
    public string? Cabys { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Unit { get; set; } = "unidad";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal TaxRate { get; set; } = 13m;
    /// <summary>El precio no se muestra en el catálogo; el cliente debe solicitar cotización.</summary>
    public bool QuoteOnly { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Visible en el catálogo público (API para webs externas).</summary>
    public bool IsPublic { get; set; } = true;
    /// <summary>Ids de adjuntos (imágenes). La primera es la principal.</summary>
    public List<string> ImageIds { get; set; } = new();
    public int Stock { get; set; } = -1;   // -1 = sin control de inventario

    public string? MainImageId => ImageIds.FirstOrDefault();
}

public class ProductCategory : Entity
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#579bfc";
    public string? Description { get; set; }
    public int Sort { get; set; }
}
