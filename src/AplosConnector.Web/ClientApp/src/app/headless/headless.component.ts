import { Component, OnInit, OnDestroy } from '@angular/core';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-headless',
  templateUrl: './headless.component.html',
  styleUrls: ['./headless.component.css']
})
export class HeadlessComponent implements OnInit, OnDestroy {
  subnav = [
    {
      navText: "History",
      routerLink: "sync-history"
    },
    {
      navText: "Settings",
      routerLink: "manage-connections"
    }
  ];
  constructor(private auth: AuthService) { }

  ngOnInit() {
    this.auth.setHeadlessMode(true);
  }

  ngOnDestroy(): void {
    this.auth.setHeadlessMode(false);
  }


}
