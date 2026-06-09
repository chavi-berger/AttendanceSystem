using FluentValidation;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ManualCorrection;

public class ManualCorrectionCommandValidator : AbstractValidator<ManualCorrectionCommand>
{
    public ManualCorrectionCommandValidator()
    {
        RuleFor(x => x.LogId)
            .NotEmpty().WithMessage("LogId is required");

        RuleFor(x => x.CorrectedByEmployeeId)
            .NotEmpty().WithMessage("CorrectedByEmployeeId is required");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters");

        // At least one of the two timestamps must be supplied, otherwise there is nothing to correct.
        RuleFor(x => x)
            .Must(x => x.NewClockIn.HasValue || x.NewClockOut.HasValue)
            .WithMessage("At least one of NewClockIn or NewClockOut must be provided");

        // If both are supplied, clock-out must be after clock-in.
        RuleFor(x => x.NewClockOut)
            .Must((cmd, newClockOut) => newClockOut > cmd.NewClockIn)
            .When(x => x.NewClockIn.HasValue && x.NewClockOut.HasValue)
            .WithMessage("NewClockOut must be after NewClockIn");
    }
}
