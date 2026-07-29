using FluentValidation;
using Wasil.Service.DTOs;

namespace Wasil.Service.Validators;

public class CreateAddressDtoValidator : AbstractValidator<CreateAddressDto>
{
    public CreateAddressDtoValidator()
    {
        RuleFor(x => x.Street)
            .NotEmpty().WithMessage("Street is required.")
            .MaximumLength(150).WithMessage("Street must not exceed 150 characters.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100).WithMessage("City must not exceed 100 characters.");

        RuleFor(x => x.ZipCode)
            .NotEmpty().WithMessage("ZipCode is required.")
            .MaximumLength(20).WithMessage("ZipCode must not exceed 20 characters.");
    }
}
