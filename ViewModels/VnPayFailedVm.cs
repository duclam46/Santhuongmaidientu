using System;
using System.Collections.Generic;

namespace DOAN_BANHANG.ViewModels;

public sealed class VnPayFailedVm
{
    public List<string> OrderCodes { get; set; } = new();
    public DateTime PaymentDeadlineUtc { get; set; }
    public string ResponseCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
