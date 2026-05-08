import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { configureApiClient } from './api';

export function ApiClientBridge() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  useEffect(() => configureApiClient({ navigate, queryClient }), [navigate, queryClient]);

  return null;
}
