using FluentValidation;
using Wasil.Service.DTOs;

namespace Wasil.Service.Validators;

public class CreateOrderDtoValidator : AbstractValidator<CreateOrderDto>
{
    public CreateOrderDtoValidator()
    {
        RuleFor(x => x.CustomerId)
            .GreaterThan(0).WithMessage("Valid CustomerId is required.");

        RuleFor(x => x.StoreId)
            .GreaterThan(0).WithMessage("Valid StoreId is required.");

        RuleFor(x => x.AddressId)
            .GreaterThan(0).WithMessage("Valid AddressId is required.");

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage("Invalid payment method.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("Order must contain at least one line.");

        RuleForEach(x => x.Lines).SetValidator(new CreateOrderLineDtoValidator());
    }
}

public class CreateOrderLineDtoValidator : AbstractValidator<CreateOrderLineDto>
{
    public CreateOrderLineDtoValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("Valid ProductId is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0.");
    }
}
