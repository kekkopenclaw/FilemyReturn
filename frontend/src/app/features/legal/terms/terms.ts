import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Terms of Service (Phase 10): the public, product-accurate terms for
 * DocuArchive — secure AI document digitisation and archive management.
 * Static content only: no API calls, no personal data collected on this
 * page. Contact details are a clearly-marked placeholder for the site
 * owner to fill in — no business email or registration number is
 * invented here.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-terms',
  styleUrl: '../legal.css',
  templateUrl: './terms.html',
})
export class Terms {}
