import { Pipe, PipeTransform } from "@angular/core";
import { AplosAccount } from '../services/aplos.service';

@Pipe({
  name: 'aplosAccount'
})
export class AplosAccountPipe implements PipeTransform {
  transform(aplosAccount: AplosAccount): string {
    return `${aplosAccount.id} - ${aplosAccount.name}`;
  }
}
