import { Component, OnInit } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-handle-pex-jwt',
  templateUrl: './handle-pex-jwt.component.html',
  styleUrls: ['./handle-pex-jwt.component.css']
})
export class HandlePexJwtComponent implements OnInit {

  constructor(private activatedRoute: ActivatedRoute, private router: Router, private auth: AuthService) { }

  ngOnInit() {
    this.activatedRoute.queryParamMap.subscribe(params => {
      const jwt = params.get('jwt');
      this.auth.exchangeJWTForSession(jwt).subscribe(
        tokenModel =>{
          console.log('got session', tokenModel);
          const session = tokenModel.token;
          this.auth.login(session).subscribe(() => {
            console.log('navigating away');
            this.router.navigate(['headless', 'connect']);
          });
        }
      );
    });
  }

}
