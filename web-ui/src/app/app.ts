import { Component } from '@angular/core';
import { BoyScoutPageComponent } from './boy-scout-page.component';

@Component({
  imports: [BoyScoutPageComponent],
  selector: 'app-root',
  template: '<app-boy-scout-page />',
})
export class App {}
