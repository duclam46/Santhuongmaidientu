namespace DOAN_BANHANG.ViewModels;

public class CategoryFilterVm
{
    public int CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int ProductCount { get; set; }

    public bool IsSelected { get; set; }
}
