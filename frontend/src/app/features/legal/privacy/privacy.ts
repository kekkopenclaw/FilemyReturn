import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Privacy Policy (Phase 10): the public, product-accurate privacy
 * notice for DocuArchive. Static content only: no API calls, no
 * personal data collected on this page. Contact details are a
 * clearly-marked placeholder for the site owner to fill in — no
 * business email or registration number is invented here.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-privacy',
  styleUrl: '../legal.css',
  templateUrl: './privacy.html',
})
export class Privacy {}
