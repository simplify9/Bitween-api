using FluentValidation;

namespace SW.Bitween
{
    public static class PasswordValidationExtensions
    {
        // Shared server-side password policy so every password-setting path
        // (create account, change password) enforces the exact same rule.
        public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().WithMessage("Password is required.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
                .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
                .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
                .Matches("[0-9]").WithMessage("Password must contain a number.")
                .Matches("[^A-Za-z0-9]").WithMessage("Password must contain a special character.");
    }
}
