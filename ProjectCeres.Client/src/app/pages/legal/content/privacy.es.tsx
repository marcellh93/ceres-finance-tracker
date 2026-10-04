/**
 * Política de Privacidad (Español) — BORRADOR.
 *
 * Basado en el código real (nombres de cookies, categorías de datos, plazos
 * de conservación) y docs/legal.md. Cada hecho a continuación es
 * verificable; cada juicio legal o de negocio que no lo es se marca como
 * [CONFIRM WITH COUNSEL: ...] y nunca debe rellenarse con una suposición.
 * No publicar como política vigente hasta que un abogado la revise.
 */
export default function PrivacyContentEs() {
  return (
    <>
      <p>
        <strong>
          [DRAFT — generado a partir del código, NO revisado legalmente. No
          publicar como política vigente hasta que un abogado la haya
          revisado.]
        </strong>
      </p>

      <p>
        Fecha de entrada en vigor: [CONFIRM: effective date]. Esta política
        explica qué datos personales recoge Project Ceres, por qué, durante
        cuánto tiempo se conservan, con quién se comparten y qué derechos
        tienes sobre ellos.
      </p>

      <h2>Qué datos recogemos</h2>
      <p>Recogemos las siguientes categorías de datos personales:</p>
      <ul>
        <li>La dirección de correo electrónico de tu cuenta.</li>
        <li>
          Tu dirección IP y la cadena de user-agent, registradas en los
          registros de seguridad: intentos fallidos de inicio de sesión,
          registros de sesiones activas, el registro de auditoría y los
          registros de IP bloqueadas.
        </li>
        <li>
          Los nombres de archivo de los adjuntos que subes, y los propios
          archivos adjuntos (por ejemplo, recibos o facturas).
        </li>
        <li>
          Tus datos financieros: cuentas, transacciones, presupuestos y
          categorías.
        </li>
      </ul>

      <h2>Por qué los tratamos (base legal)</h2>
      <p>
        Nos basamos en las siguientes bases legales para el tratamiento, de
        acuerdo con el artículo 6 del RGPD:
      </p>
      <ul>
        <li>
          [CONFIRM WITH COUNSEL: legal basis — service delivery (account,
          transactions, budgets, categories) = contract]
        </li>
        <li>
          [CONFIRM WITH COUNSEL: legal basis — financial-record retention =
          legal obligation]
        </li>
        <li>
          [CONFIRM WITH COUNSEL: legal basis — security logs (failed
          sign-ins, sessions, audit log, blocked IPs) = legitimate interest]
        </li>
      </ul>

      <h2>Durante cuánto tiempo los conservamos</h2>
      <p>Conservamos los datos durante los siguientes plazos:</p>
      <ul>
        <li>
          Informes guardados eliminados temporalmente: 90 días, tras los
          cuales se purgan automáticamente.
        </li>
        <li>
          Registros de auditoría: 12 meses, tras los cuales se purgan
          automáticamente.
        </li>
        <li>
          Registros de intentos fallidos de inicio de sesión: 1 año, tras el
          cual se purgan automáticamente.
        </li>
        <li>
          Cierre de cuenta sin solicitud de supresión (baja natural): un
          período de gracia de 30 días con reactivación completa, seguido de
          un archivo sellado de 150 días, seguido de la eliminación
          permanente en el día 180.
        </li>
        <li>
          Cierre de cuenta con solicitud de supresión conforme al RGPD: los
          identificadores personales se anonimizan de forma inmediata tras
          un período de retención de solo cancelación de 72 horas (recibirás
          un enlace por correo electrónico para cancelar la solicitud dentro
          de ese plazo; transcurridas 72 horas, la retención no puede
          revertirse).
        </li>
      </ul>
      <p>
        <strong>Los registros financieros se conservan durante un plazo
        superior al anterior, y esto prevalece sobre el derecho de
        supresión.</strong> La legislación española (Código de Comercio,
        art. 30) exige conservar los registros contables durante 6 años, y
        los documentos con relevancia fiscal (Ley General Tributaria)
        durante 4 a 6 años. El artículo 17(3)(b) del RGPD permite esto: exime
        de la conservación legalmente exigida al derecho de supresión. En la
        práctica, si solicitas la supresión, tus identificadores personales
        (nombre, correo electrónico y otros identificadores directos) se
        anonimizan, pero los registros financieros anonimizados subyacentes
        —cuentas, transacciones, presupuestos, categorías— se conservan
        durante el plazo legal de conservación descrito anteriormente antes
        de su eliminación.
      </p>

      <h2>Con quién los compartimos</h2>
      <p>
        No utilizamos analítica de comportamiento ni rastreadores
        publicitarios de terceros en la fase actual del producto. Los datos
        solo se comparten con los proveedores de servicios necesarios para
        operar la aplicación:
      </p>
      <ul>
        <li>
          [CONFIRM: hosting provider — name + confirmation a Data Processing
          Agreement (DPA) has been signed]
        </li>
        <li>
          [CONFIRM: email service provider — name + confirmation a Data
          Processing Agreement (DPA) has been signed]
        </li>
      </ul>

      <h2>Tus derechos</h2>
      <p>
        Conforme al RGPD, tienes derecho a acceder, rectificar, suprimir,
        portar y oponerte al tratamiento de tus datos personales. En la
        aplicación:
      </p>
      <ul>
        <li>
          <strong>Acceso y portabilidad:</strong> ve a Configuración →
          Cuenta y utiliza la opción para descargar una copia de tus datos.
        </li>
        <li>
          <strong>Rectificación:</strong> actualiza tu información
          directamente a través de las pantallas normales de la cuenta y de
          introducción de datos.
        </li>
        <li>
          <strong>Supresión:</strong> ve a Configuración → Cuenta y solicita
          el cierre de la cuenta con supresión. Recibirás un correo con un
          enlace de cancelación válido durante 72 horas; transcurrido ese
          plazo, la anonimización procede según lo descrito anteriormente.
        </li>
        <li>
          <strong>Oposición y limitación:</strong> contáctanos utilizando
          los datos a continuación.
        </li>
      </ul>

      <h2>Cómo contactarnos</h2>
      <p>
        Para ejercer cualquiera de los derechos anteriores, o para cualquier
        pregunta sobre esta política: [CONFIRM WITH COUNSEL: contact email
        address]. Nuestro Delegado de Protección de Datos y la autoridad de
        control para reclamaciones: [CONFIRM WITH COUNSEL: DPO contact
        details and supervisory-authority (AEPD) contact details].
      </p>
    </>
  );
}
