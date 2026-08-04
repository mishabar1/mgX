import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';
import { GeneralService } from './general.service';

/**
 * Attaches the JWT as `Authorization: Bearer <token>` to every outgoing HTTP request.
 * Registered in app.module.ts via HTTP_INTERCEPTORS.
 */
@Injectable()
export class AuthInterceptor implements HttpInterceptor {

  constructor(private general: GeneralService) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const token = this.general.Token;
    if (token) {
      req = req.clone({
        setHeaders: { Authorization: `Bearer ${token}` }
      });
    }
    return next.handle(req);
  }
}
