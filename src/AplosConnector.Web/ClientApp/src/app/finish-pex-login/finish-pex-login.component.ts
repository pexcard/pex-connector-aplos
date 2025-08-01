import { Component, OnInit, Inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
    selector: 'app-finish-pex-login',
    templateUrl: './finish-pex-login.component.html',
    styleUrls: ['./finish-pex-login.component.css'],
    standalone: false
})
export class FinishPexLoginComponent implements OnInit {

  constructor(private activatedRoute: ActivatedRoute, private router: Router, private auth: AuthService) { }

  ngOnInit() {
    this.activatedRoute.params.subscribe(params => {
      const sessionId = params.sessionId;
      this.auth.login(sessionId).subscribe(() => {
        console.log('navigating away');
        this.router.navigate(['connect']);
      });
    });
  }

}
