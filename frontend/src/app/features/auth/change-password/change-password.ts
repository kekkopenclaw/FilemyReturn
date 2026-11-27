import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth.service';

/** Both password fields must match. */
const passwordsMatch: ValidatorFn = (
  group: AbstractControl,
): ValidationErrors | null => {
  const password = group.get('newPassword')?.value as string | undefined;
  const confirm = group.get('confirmPassword')?.value as string | undefined;
  return password && confirm && password !== confirm
    ? { passwordsMismatch: true }
    : null;
};

/** Problem detail from the backend error response, if any. */
function errorDetail(err: unknown): string | null {
  if (err instanceof HttpErrorResponse) {
    const detail = (err.error as { detail?: unknown } | null)?.detail;
    return typeof detail === 'string' && detail.length > 0 ? detail : null;
  }
  return null;
}

/**
 * Change-password page. Reachable without signing in for the first-login
 * flow: the backend only allows an unauthenticated change when the account
 * is flagged MustChangePassword (e.g. the seeded admin). Signed-in users
 * can also change their own password here.
 */
@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './change-password.html',
  styleUrl: '../login/login.css',
})
export class ChangePassword {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected readonly loading = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly success = signal(false);
  protected readonly mustChange = signal(false);

  protected readonly form = this.fb.group(
    {
      email: ['', [Validators.required, Validators.email]],
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(12)]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: passwordsMatch },
  );

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    const email =
      params.get('email') ?? this.auth.currentUser()?.email ?? '';
    if (email) {
      this.form.controls.email.setValue(email);
    }
    if (params.get('reason') === 'must-change-password') {
      this.mustChange.set(true);
    }
  }

  protected onSubmit(): void {
    if (this.form.invalid || this.loading()) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading.set(true);
    this.serverError.set(null);
    const { email, currentPassword, newPassword } = this.form.getRawValue();
    this.auth
      .changePassword(email ?? null, currentPassword!, newPassword!)
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.success.set(true);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.serverError.set(
            err instanceof HttpErrorResponse && err.status === 401
              ? 'The current password is incorrect.'
              : err instanceof HttpErrorResponse && err.status === 403
                ? 'Password change is not allowed for this account.'
                : err instanceof HttpErrorResponse && err.status === 423
                  ? 'Your account is locked after too many failed attempts. Try again later.'
                  : err instanceof HttpErrorResponse && err.status === 400
                    ? (errorDetail(err) ??
                      'The current password is incorrect, or the new password does not meet the requirements.')
                    : 'Could not change the password. Please check your connection and try again.',
          );
        },
      });
  }
}
