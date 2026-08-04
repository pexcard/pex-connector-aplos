import { Component, Input } from '@angular/core';

@Component({
  standalone: false,
  selector: 'app-status-icon',
  template: `
    @if (value) {
      <cds-icon shape="success-standard" solid status="success" [size]="size"></cds-icon>
    }
    @if (!value) {
      <cds-icon shape="ban" solid status="danger" [size]="size"></cds-icon>
    }
    `
})
export class StatusIconComponent {
  @Input() value: boolean = false;
  @Input() size: string = 'sm';
}
