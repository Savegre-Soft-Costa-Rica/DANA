# Política de seguridad

Si encuentras una vulnerabilidad, **no abras un issue público**. Escribe a los mantenedores (ver perfil del repositorio) con una descripción, pasos para reproducirla y el impacto. Responderemos en un plazo razonable y coordinaremos la publicación del arreglo.

Recomendaciones de despliegue: usa HTTPS detrás de un proxy, cambia `MONGO_PASSWORD`, no expongas el puerto de MongoDB, mantén `ALLOW_REGISTRATION=false` tras crear tus usuarios y guarda las claves de Google/SMTP fuera del repositorio.
