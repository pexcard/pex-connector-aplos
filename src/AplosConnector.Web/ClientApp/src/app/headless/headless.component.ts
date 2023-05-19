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
      navText: "Manage sync",
      routerLink: "sync-manage"
    },
    {
      navText: "Manage vendor cards",
      routerLink: "vendors-manage"
    }
  ];
  constructor(private auth: AuthService) { }

  ngOnInit() {
    this.auth.setHeadlessMode(false);
  }

  ngOnDestroy(): void {
    this.auth.setHeadlessMode(false);
  }


}
