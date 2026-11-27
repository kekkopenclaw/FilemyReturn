import { Component, input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  templateUrl: './empty-state.html',
  styleUrl: './empty-state.css',
})
export class EmptyState {
  /** Main message shown to the user. */
  readonly message = input.required<string>();
  /** Optional supporting detail line. */
  readonly detail = input<string>('');
}
