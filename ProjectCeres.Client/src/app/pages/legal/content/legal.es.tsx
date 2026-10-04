/**
 * Aviso Legal + Política de Cookies (Español) — BORRADOR.
 *
 * Basado en el código real (nombres y propósitos de las cookies) y
 * docs/legal.md. Los datos de identidad del negocio (Aviso Legal) no son
 * verificables a partir del código y se dejan como marcadores
 * [CONFIRM WITH COUNSEL: ...] — nunca inventados. No publicar como política
 * vigente hasta que un abogado la haya revisado.
 */
export default function LegalContentEs() {
  return (
    <>
      <p>
        <strong>
          [DRAFT — generado a partir del código, NO revisado legalmente. No
          publicar como política vigente hasta que un abogado la haya
          revisado.]
        </strong>
      </p>

      <p>Fecha de entrada en vigor: [CONFIRM: effective date].</p>

      <section id="aviso-legal">
        <h2>Aviso legal</h2>
        <p>
          De conformidad con el artículo 10 de la LSSI-CE (Ley 34/2002, de
          Servicios de la Sociedad de la Información y de Comercio
          Electrónico), se facilita la siguiente información identificativa
          del responsable de este sitio web y servicio:
        </p>
        <ul>
          <li>Denominación de la entidad: [CONFIRM: legal entity name]</li>
          <li>Número de identificación fiscal (NIF): [CONFIRM: NIF]</li>
          <li>Domicilio social: [CONFIRM: registered address]</li>
          <li>Correo electrónico de contacto: [CONFIRM: contact email]</li>
        </ul>
      </section>

      <section id="cookies">
        <h2>Política de cookies</h2>
        <p>
          Project Ceres utiliza las siguientes cookies. Todas ellas son
          estrictamente necesarias para el funcionamiento del sitio y
          ninguna se utiliza con fines de seguimiento o publicidad; ninguna
          está sujeta a consentimiento hoy en día, porque ninguna es
          opcional.
        </p>
        <ul>
          <li>
            <strong>__Host-Session</strong> — mantiene tu sesión iniciada.
            Se elimina cuando finaliza la sesión.
          </li>
          <li>
            <strong>__Host-Persist</strong> — mantiene tu sesión iniciada
            entre visitas cuando eliges "recordarme."
          </li>
          <li>
            <strong>__Host-XSRF</strong> — protege las solicitudes que
            modifican el estado frente a la falsificación de solicitudes
            entre sitios (CSRF).
          </li>
          <li>
            <strong>lang</strong> — recuerda tu idioma de visualización
            preferido (inglés/español).
          </li>
          <li>
            <strong>cookie_consent</strong> — registra la elección que
            hiciste en el propio banner de cookies, para no volver a
            preguntarte innecesariamente.
          </li>
        </ul>
        <p>
          Dado que todas las cookies anteriores son estrictamente
          necesarias, hoy en día no existen cookies opcionales ni analíticas
          para las que dar tu consentimiento. Si esto cambia en el futuro,
          esta política y el banner de consentimiento se actualizarán antes
          de activar cualquier cookie nueva.
        </p>
        <p>
          Puedes retirar o cambiar tu elección de cookies en cualquier
          momento, con la misma facilidad con la que la diste: utiliza
          "Gestionar preferencias de cookies" en el pie de página de este
          sitio, o la opción equivalente en Configuración → Privacidad y
          cookies si has iniciado sesión, para volver a abrir el banner de
          consentimiento.
        </p>
      </section>
    </>
  );
}
