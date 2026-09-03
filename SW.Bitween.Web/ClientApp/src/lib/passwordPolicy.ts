// Client-side mirror of the server password policy. The server is the enforcement
// point; this just gives the user faster feedback with the same messages.
// Keep in sync with PasswordValidationExtensions on the backend.
export const validatePassword = (password: string | undefined): string[] => {
  const p = password ?? "";
  const errors: string[] = [];
  if (p.length < 8) errors.push("Password must be at least 8 characters.");
  if (!/[A-Z]/.test(p)) errors.push("Password must contain an uppercase letter.");
  if (!/[a-z]/.test(p)) errors.push("Password must contain a lowercase letter.");
  if (!/[0-9]/.test(p)) errors.push("Password must contain a number.");
  if (!/[^A-Za-z0-9]/.test(p)) errors.push("Password must contain a special character.");
  return errors;
};

/** The rule, phrased for a form hint. */
export const PASSWORD_HINT =
  "At least 8 characters, with an uppercase letter, a lowercase letter, a number and a special character.";
