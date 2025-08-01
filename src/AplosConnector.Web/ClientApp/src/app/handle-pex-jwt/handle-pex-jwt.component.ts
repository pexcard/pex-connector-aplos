import { Component, OnInit } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { concatMap } from 'rxjs/operators';

@Component({
    selector: 'app-handle-pex-jwt',
    templateUrl: './handle-pex-jwt.component.html',
    styleUrls: ['./handle-pex-jwt.component.css'],
    standalone: false
})
export class HandlePexJwtComponent implements OnInit {
  loginFailed = false;

  constructor(private activatedRoute: ActivatedRoute, private router: Router, private auth: AuthService) { }

  ngOnInit() {
    const jwt = this.activatedRoute.snapshot.queryParams.jwt;
    this.auth.exchangeJWTForSession(jwt)
      .pipe(
        concatMap((token) => this.auth.login(token.token)),
      ).subscribe({
        next: () => {
          void this.router.navigate(['connect']);
        },
        error: (error) => {
          console.error('Error handling PEX jwt login:', error);
          this.loginFailed = true;
        }
      });
  }

}
