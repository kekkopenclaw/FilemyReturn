import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Public landing page (DocuArchive home page).
 *
 * Purely presentational: every button is a real link (log in, register, or
 * an on-page section). A route guard keeps signed-in users out — they go
 * to /dashboard instead.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-landing',
  styleUrl: './landing.css',
  templateUrl: './landing.html',
})
export class Landing {}
