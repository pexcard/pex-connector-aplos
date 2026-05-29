import { Component, Input } from '@angular/core';

@Component({
  standalone: false,
  selector: 'app-status-icon',
  template: `
    <cds-icon *ngIf="value"  shape="success-standard" solid status="success" [attr.size]="size"></cds-icon>
    <cds-icon *ngIf="!value" shape="ban"              solid status="danger"  [attr.size]="size"></cds-icon>
  `
})
export class StatusIconComponent {
  @Input() value: boolean = false;
  @Input() size: string = 'sm';
}
