using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record PaymentInput(PaymentMethod Method, decimal Amount);

public sealed record PaymentDetail(PaymentMethod Method, decimal Amount, decimal Tendered);
