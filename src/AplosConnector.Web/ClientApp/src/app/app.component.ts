import { Component, OnInit, OnDestroy } from '@angular/core';
import { AuthService } from './services/auth.service';
import { Subscription, BehaviorSubject } from 'rxjs';
import { Router } from '@angular/router';
import { FundingSource } from './services/mapping.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'PEX Connector for Aplos';
  isLoggedIn = false;
  businessName: string = null;
  isPrepaid:boolean = false;
  isCredit:boolean = false;

  constructor(private auth: AuthService, private router: Router) {
  }

  loginSubscription$: Subscription;

  ngOnInit(): void {
    this.loginSubscription$ = this.auth.sessionId.subscribe(
      token => {
        console.log('login state changed', token);
        if (token) {
          this.isLoggedIn = true;
        } else {
          this.isLoggedIn = false;
        }
      }
    );

    this.auth.businessName.subscribe(
      name => this.businessName = name
    );

    this.auth.fundingSource.subscribe(
      fundingSource => {
        this.isPrepaid = fundingSource == FundingSource.Prepaid;
        this.isCredit = fundingSource == FundingSource.Credit;
      }
    );

    this.auth.autoLogIn();
  }

  onLogout() {
    this.auth.logout().subscribe(() => {
      this.router.navigate(['/', 'login']);
    });
  }

  ngOnDestroy(): void {
    this.loginSubscription$.unsubscribe();
  }

}
