import { Component, OnInit, Inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-finish-pex-login',
  templateUrl: './finish-pex-login.component.html',
  styleUrls: ['./finish-pex-login.component.css']
})
export class FinishPexLoginComponent implements OnInit {

  constructor(private activatedRoute: ActivatedRoute, private router: Router, private auth: AuthService) { }

  ngOnInit() {
    this.auth.logout();

    let sessionId: string;
    this.activatedRoute.params.subscribe(params => {
      sessionId = params.sessionId;
    });

    this.auth.login(sessionId);

    console.log('navigating away');
    this.router.navigate(['connect']);
  }

}
