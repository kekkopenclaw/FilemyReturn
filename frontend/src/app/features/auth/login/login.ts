import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth.service';

/** Machine-readable error code from the backend problem response, if any. */
function errorCode(err: unknown): string | null {
  if (err instanceof HttpErrorResponse) {
    const code = (err.error as { code?: unknown } | null)?.code;
    return typeof code === 'string' ? code : null;
  }
  return null;
}

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly loading = signal(false);
  protected readonly serverError = signal<string | null>(null);

  protected readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected onSubmit(): void {
    if (this.form.invalid || this.loading()) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading.set(true);
    this.serverError.set(null);
    const { email, password } = this.form.getRawValue();
    this.auth.login(email!, password!).subscribe({
      next: () => {
        this.loading.set(false);
        void this.router.navigate(['/dashboard']);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        if (
          err instanceof HttpErrorResponse &&
          err.status === 403 &&
          errorCode(err) === 'MustChangePassword'
        ) {
          // Seeded admin / first login: no tokens were issued. Send the
          // user to the change-password page with their email prefilled.
          void this.router.navigate(['/change-password'], {
            queryParams: {
              email: email ?? '',
              reason: 'must-change-password',
            },
          });
          return;
        }
        this.serverError.set(
          err instanceof HttpErrorResponse && err.status === 401
            ? 'Incorrect email or password.'
            : err instanceof HttpErrorResponse && err.status === 423
              ? 'Your account is locked after too many failed sign-in attempts. Try again later.'
              : 'Could not sign in. Please check your connection and try again.',
        );
      },
    });
  }
}
